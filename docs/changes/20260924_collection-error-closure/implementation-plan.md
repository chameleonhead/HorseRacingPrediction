# 実装・統合・復旧の実行仕様

本書は [README](README.md) のACとtask planを具体化する実装契約案である。Statusと承認はREADMEに従う。文書を詳しくしたことを実装・配備の承認と解釈しない。

## 1. 今回作るものと、作らないもの

成果物は「既存修正と対象の残存原因への必要な改修を含む収集コード」「反例を含む回帰テスト」「対象限定の復旧と本番解消証拠」である。調査・既存patch統合だけでは今回の変更を完了しない。監視システムの作り直し、汎用自動修復エンジン、新しい管理画面は作らない。

最初の実装対象は既存契約を満たすための不足・回帰とする。既に満たす箇所はコードを変更せず、統合revision上のテスト証拠を残す。現在の停止原因やOwner問題を仮説だけで修正しない。その枝はT1の証拠確認後にT2fで設計・改修・検証まで進める。ID/aliasモデルや停止policy変更が必要なら専用recordの設計・再承認を先に行うが、本変更の成果範囲から外さない。

### 実装前に確認した基準点

| 対象 | 確認値・コード上の事実 | 実装時の判断 |
| --- | --- | --- |
| 現在checkout | `d9710e38c05f44562b326540d23902e00be602c5`。Web/Collector等に既存未コミット変更あり | このcheckoutを配備物の生成元にしない。既存変更をstage/revertしない |
| remote main（2026-09-24確認） | `ff95b22499312462194a970b038dcc9d959163bd`、PR #64 merge | 実行時に再照合し、承認されたclean revisionを実装基準にする |
| PR #65 | merge `3bba5b2ecd87bab9caf0f4cb70f7076926ce3dd6`。Result選択、Card/Result独立、振替、lease隔離、DB schemaを含む | ローカルの古いファイルを上書きコピーしない。PR単位の依存を確認 |
| metadata回復 | local commit `e036a6b8`、merge `c1a5bc36`。`RequestCoreAsync`からmetadata解決へ接続済み | remote/mainとの差分は実コードで確認。記録不在だけで未実装としない |
| local wake経路 | `CollectionLambdaInvocation.ExecuteWakeAsync`のloopは`worker.ExecuteAsync`を直接await | PR #65のcatch/後続継続を統合時に失わない反例が必須 |
| local Result選択 | `JraRaceLinkSelector.FindUrl`はURL group内のラベルを合成 | PR #65の要素identity修正を採用し、同一fragment誤選択を反例化 |
| 本番revision | 今回の文書作成では未取得 | remote mainと一致すると仮定しない。API/Collector双方を別々に確認 |

上記は作業開始revisionの固定指示ではない。実行時の最新証拠で基準を更新し、差分理由を記録する。

## 2. 実経路と変更責務

```text
discovery / 手動再取得 / Recovery
  → API入力・主体preflight
  → Store: request / immutable task metadata / outbox
  → Dispatcher: priority・互換性・generation
  → wake / execution lease取得
  → worker: task lease取得
  → handler: Card / Result / profile
  → 対象identity検証 → domain write
  → complete: facet・attempt・failure impact
  → 次task進行 / 有限retry / 対象隔離 / 安全停止
```

変更責務は下記の単位で固定する。全パスはrepository root相対。共有ファイルの同時編集は禁止。

| Slice | 読み取り・変更候補 | 実装方針とwrite owner |
| --- | --- | --- |
| S1 Result選択 | `src/HorseRacingPrediction.Scraping/Jra/JraRaceLinkSelector.cs`、`Navigation/JraNavigator.cs`（同Jra配下） | T2a worker。既存PR #65を基準に不足だけ修正。公開戻り値契約の変更はMainへ戻す |
| S2 Race段階 | `src/HorseRacingPrediction.Collector/CollectionPlatform/JraRaceCollectionHandlers.cs`、`JraRaceDetailUrl.cs` | T2b Main。facet、due、identity、replacementは一体で判断 |
| S3 再取得metadata | `src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/CollectionPlatformStore.cs` | T2c Main。永続化と既存task不変性を保持 |
| S4 実行競合・停止 | `src/HorseRacingPrediction.Collector/CollectionPlatform/CollectionLambdaInvocation.cs`、`CollectionPlatformWorkerClient.cs`、`src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/CollectionPlatformStore.cs` | T2d Main。worker transport、lease、failure impactは低コストworkerに設計させない。S3と直列 |
| S5 主体整合 | `src/HorseRacingPrediction.ApiClient/OwnerIdentityContract.cs`、`src/HorseRacingPrediction.Api/EndpointExtensions.cs`の`BuildOwnersAsync`とOwner GET、`src/HorseRacingPrediction.Collector/CollectionPlatform/JraSubjectCollectionHandlers.cs`の`CollectAsync` / `OwnerIdentityApiClient.ExistsAsync` | T1c read-only調査→Main判定。未知のOwner補正を包括的write scopeに含めない |
| S6 既存dispatch修正 | `src/HorseRacingPrediction.Api/CollectionController/CollectionPlatformOutboxDispatcher.cs`、`CollectionMonitoringService.cs`、CollectionOperationsの`CollectionPlatformStore.Monitoring.cs`、`CollectionMonitoringModels.cs` | PR #64の契約を保持。新しい順序アルゴリズムは作らない |

## 3. T1: 調査を実装判断へ変える仕様

### 入力・出力

実行時の最新snapshot、pipeline状態、障害group、代表resourceのtask/attempt履歴、execution batch、API/Collector配備revisionを照合する。18:20の29件は起点であって作業中の最新状態ではない。

`evidence/baseline.md`（実行時作成）に、秘密情報を除去した次の項目を記録する。

- 観測時刻JST/UTC、environment、取得成功/失敗、snapshot cutoff、ページング/件数上限による不足。
- 停止理由・停止時刻・代表task/attempt/definition、result/error code、実行batch相関。
- repository基準SHA、API配備SHA、Collector image digest/SHA、schema識別子。取得できない欄はunknownとする。
- 原因別の新規失敗件数、due backlog、実行開始/成功完了件数、最古due age。異なる時刻の値を直接比較しない。
- 原因ごとの `existing-fix / local-only / deployed-unverified / new-defect / expected-wait / unavailable / unresolved` の分析分類。これはtask stateの代替ではない。
- 次の操作、既存Codex task ID、dedupe key、該当AC、必要な承認。

### 読み取りAPI契約

既存runnerが使用可能な場合にだけ、既存承認範囲のGET診断を行う。runnerで取得できない詳細は、許可された解消taskが診断skillの資格情報境界に従って取得する。本監視スレッドから独自の本番writeを行わない。

| 用途 | 既存route（`/api/admin/collection`配下） | 注意 |
| --- | --- | --- |
| 停止確認 | `GET /pipeline` | intentional pauseとfailure pauseを分ける |
| 代表障害 | `GET /failure-notifications/groups/{groupKey}` | finding fingerprintをgroupKeyとして代用しない |
| task/attempt | `GET /resources/{type}/{provider}/{resourceId}/{definition}` | history page/sizeを指定し、最新1件だけで原因確定しない |
| batch相関 | `GET /execution-batches/{executionBatchId}` | 隣接taskが成功したか、全batchが中断したかを区別 |
| 全体進行 | `GET /progress`、`GET /dashboard` | 表示上のRunning件数だけでは進行証拠にならない |

診断はGETのみ。`/migrations/race-detail/preview`は現行実装ではPOSTであり、名前がpreviewだからGET診断として実行してはいけない。migration対象の別途承認と実装の副作用確認が必要。

### 実装への分岐

| 判定 | 次の処理 | 禁止する近道 |
| --- | --- | --- |
| 修正が未統合/未配備 | T2で既存patch依存を統合・回帰、T3で許可後配備 | 同じ機能の再作成 |
| 修正配備済み・旧失敗だけ残存 | 現在状態の再検証後、対象限定Recovery候補へ | 履歴削除や全group一括retry |
| 同一入力・修正revisionでも再現 | 原因別の失敗fixtureを作り既存契約の回帰を修正 | 原因が変わったのに過去のdedupe keyへ無条件集約 |
| 未公開/振替未確定/アクセス制限 | 既存のdue/backoff/停止契約を維持し、正常処理を妨げていないか確認 | 曜日だけの終了判断、成功偽装 |
| 不明・証拠欠落・Owner同一性未証明 | 不足証拠と専用設計gateを提示 | 名前だけでID補正、未知例外をisolatedへ変更 |

## 4. T2: 固定する動作・反例

### S1: Resultリンク

1. 期待する日付・競馬場・R番号を解析できる直接Result URLを優先する。
2. 同一URLを持つ「検索」「オッズ」「対象R」要素のラベルを合成しない。選択した要素identityをnavigationまで渡す。
3. fragment-onlyの場合も対象Rの同じ要素をクリックする。
4. 遷移後のページ種別とRace identityを検証し、不一致ならdomain writeしない。
5. 既存PR #65の実装が合格するなら新しいselectorを作らない。

### S2: Card/Resultと振替

| 状態 | 期待する処理 |
| --- | --- |
| 結果確認時刻前・Card未公開 | 既存のavailability wait。Resultには進まない |
| 結果確認時刻後・Card取得失敗 | Cardのstage outcomeを残し、許可された既知の掲載/遷移失敗ならResultへ進む |
| Cardのdomain write失敗 | 失敗を記録し、Resultへの独立遷移とtask全体の成功判定を分ける。保存されていないCardをCurrentにしない |
| Result取得・保存成功 | Result facetを進める。Card未取得を偽って補完しない。既存の構造化部分結果/隔離契約を維持 |
| Resultが既にCurrent | Card補修失敗だけでResultを再取得・上書きしない |
| 公式の同一開催identityが別日 | PR #65のreplacement経路へ。旧予定日へ結果を保存しない |
| 代替先未確認 | 旧URLエラーだけで代替を断定せず、既存backoff待機 |

実行タイミングは既存設定を使用する。local既定はResult確認猶予5分、当日再確認10分、過去30分開始・最大360分。監視の発走予定+30分とは目的が違うので統一・短縮しない。認証/広域アクセス障害、書込整合性例外まで一律に「Card失敗だから無視」へ変えない。

PR #65の`AddRaceRescheduleLineage`はschema変更である。`MarkRaceRescheduled`からevent、read model、HTTP writeまで含めて統合し、Collectorだけを先に差し替えない。全過去Race移行は別途gateだが、新コード動作に必要なschemaは配備前提から除外しない。

### S3: metadata

`ManualRefresh` / `Recovery`で新しいtask snapshotを作る場合、同Resource/Definitionの最新非空task metadataを既定値とする。復元した辞書全体が空の場合だけResource属性へfallbackし、欠落キー単位で現在のResource属性を混ぜない。その後、明示属性をキー単位で上書きする。最新taskが空でも過去の有効snapshotを利用する。既存taskのJSONは変更しない。

通常のInitial/Discovery/Backfillは既存caller契約を維持する。異なるresourceの値を継承しない。秘密情報禁止・サイズ等の既存validationを迂回しない。空名しかない対象を架空の名称で救済しない。

履歴snapshotの選択はResource+Definition単位だが、fallback元のResource属性はDefinition別ではない。Resource属性が他definitionの登録で更新され得ることを証拠として記録し、本変更でdefinition別fallbackモデルへ拡張しない。

### S4: lease・failure impact

- `ExecuteWakeAsync`内で`CollectionTaskActiveElsewhereException`をtask単位に扱い、対象を二重実行せず、後続taskとexecution completionを継続する。既存PR #65の意味論を採用する。
- `HttpRequestException`、`TaskCanceledException`、unknown exceptionをまとめて握り潰す変更はしない。start応答不明時のfencing、取消、残り実行時間、finalize用tokenを維持する。
- 既知の対象限定エラーだけが明示的Isolated。未知・データ整合性のfailure impact既定値はStopPipelineのまま。
- TargetClosedは既存fresh-session一度retry、既存時間窓/閾値でのsystemic stopを検証する。今回、閾値やwindowを緩和しない。
- 期限切れleaseの表示修正を、leaseを回収した証拠として使わない。失効処理・generation・outboxと実行開始を照合する。

### S5: Ownerとprofileの設計境界

producerの生成ID・入力名、RaceEntry由来のOwnerName、既存alias/redirect、consumer lookupを照合する。この経路に権威あるRaceEntry OwnerIdがあるとは仮定しない。producer/consumer間で正規化やalias適用が違わないかを調べる。OwnerにはHorse/Jockey/Trainer名称正規化ツールを流用しない。

profileは既存preflightの後段AC8-AC11を採用し、廃止されたobsolete migration applyを復活させない。新しいalias、ID移行、曖昧候補の選択policyが必要なら、具体的な入力例・before/after・参照影響を専用recordに記載して承認を得る。現段階でS5の新規補正実装を承認対象としない。

localのOwner handlerはtask名を必須とし、taskのResource IDを`OwnerIdentityApiClient.ExistsAsync`で照合する。API側はcanonical/legacy IDを受理し、`BuildOwnersAsync`はOwnerName群をalias mappingまたは正規化名由来IDでまとめる。Owner成功はprofile writeでも人物の厳密な同定でもなく、現行の名前group projectionにおける存在確認である。同じ正規化名を持つ別人物の識別能力はこの経路だけでは保証されない。監視のOwner migration previewは件数/例示のみで、安全なapplyの証明ではない。入力・照合結果を代表taskで確認し、証拠不足の場合は同定未解決として残す。今回新しい自動alias/ID書換えを追加せず、この制約を隠すためにOwner失敗を成功へ変えない。

## 5. テスト責任・具体的コマンド

既存テストを「今回実行済み」とは扱わない。以下は承認後にcleanな統合worktreeで実行する。workerは最小testを変更中に回し、handoff前に該当projectの非External regressionを通す。Mainは統合後にCI相当を実行する。

| Test group | 所有ファイル・既存test/追加反例 | 合格条件 |
| --- | --- | --- |
| V1 Result | `tests/HorseRacingPrediction.Scraping.Tests/Jra/JraRaceLinkSelectorTests.cs`、`Navigation/JraNavigatorTests.cs`。`FindUrl_ResultNavigation_PrefersNumberedDirectResultOverFragmentControls`、`FindLink_DoesNotCombinePurposeAndRaceLabelsAcrossSharedFragmentUrl` | 直接URL優先、fragment要素保持、別Race/検索を選ばない |
| V2 Race | `tests/HorseRacingPrediction.Collector.Tests/CollectionPlatform/JraDirectCollectionHandlerTests.cs`、`TestSupport/FakeJraSessionInfra.cs` | `RaceDetail_ResultDue_CardUnavailable_ContinuesWithResultCollection`、`RaceDetail_ResultNotDue_CardUnavailable_WaitsWithoutNavigatingToResult`、既存Result非上書き・write rejection保持・replacement一度だけ |
| V3 metadata | 同Collector.Tests配下の`CollectionPlatform/CollectionPlatformStoreTests.cs` | `ManualRefresh_WithoutMetadata_InheritsCompletedTaskSnapshot`、`Recovery_WithoutMetadata_SkipsLatestEmptySnapshot`、`Recovery_MetadataOverridesOnlySpecifiedKeys`、元task不変・別Resource混入なし |
| V4 lease/停止 | `CollectionPlatform/CollectionLambdaInvocationTests.cs`、Store/failure関連tests | `Wake_ActiveElsewhereContinuesLaterTasksAndCompletesExecution`、既存legacy競合、取消、unknown停止、TargetClosed閾値、重複配送で二重writeなし |
| V5 identity/DB | `tests/HorseRacingPrediction.Api.Tests/RaceEndpointsTests.cs`、`tests/HorseRacingPrediction.Domain.Tests/RaceAggregateTests.cs`、`tests/HorseRacingPrediction.Infrastructure.Tests/SqliteDbContextProviderTests.cs`、既存subject repair tests | rescheduleと旧参照保持、旧SQLiteのデータ保持、schema再適用安全性、曖昧主体へのwriteなし |
| V6 統合/E2E | Scraping.Testsの`JraSiteE2ETests`、既存非External全体、API/Collector実経路 | 直近/過去Resultと現在Cardの対象identityと保存結果が一致。外部サイトに到達不能ならVerifiedにしない |

V3では`TaskMetadata_RemainsImmutableWhenLaterRequestUpdatesResourceAttributes`と`KeyedRecovery_RecreatesTaskWhenOriginallyReusedActiveTaskHasBecomeTerminal`も維持する。V4の既存反例は`IsolatedPermanentFailure_QueuesNotificationWithoutPausingPipeline`、`ClosedSessionFailures_RetryInIsolationUntilBurstThresholdThenPause`、`Pause_PersistsAcrossRestartAndBlocksAcquisitionUntilResume`。V5のOwner非回帰は`HorseEndpointsTests.OwnerDetail_ResolvesProducerCanonicalAndLegacyIds`、`CollectionMonitoringServiceTests.Inspect_DoesNotClassifyUnsupportedOwnerFailureAsKnownRecovery`と`JraSubjectCollectionHandlerTests`で照合する。名称一致だけで不存在IDが成功する反例を許容しない。

最小test（各sliceで該当行を選ぶ）:

```powershell
dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj -c Release --filter 'FullyQualifiedName~JraRaceLinkSelectorTests|FullyQualifiedName~JraNavigatorTests'
dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj -c Release --filter 'FullyQualifiedName~JraDirectCollectionHandlerTests'
dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj -c Release --filter 'FullyQualifiedName~CollectionPlatformStoreTests'
dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj -c Release --filter 'FullyQualifiedName~CollectionLambdaInvocationTests'
dotnet test tests/HorseRacingPrediction.Infrastructure.Tests/HorseRacingPrediction.Infrastructure.Tests.csproj -c Release --filter 'FullyQualifiedName~SqliteDbContextProviderTests'
```

handoff regressionは同じprojectコマンドのfilterを `TestCategory!=External` に置き換える。testが0件なら成功扱いにせず、名称・project・checkoutを確認する。PR #65由来のtestはlocal旧checkoutに存在しないため、先に統合revisionを確認する。

Mainの最終ローカルgate（`.github/workflows/app-ci.yml`に対応。Actionsは起動しない）:

```powershell
dotnet tool restore
dotnet restore HorseRacingPrediction.sln
dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes
dotnet build HorseRacingPrediction.sln --no-restore --configuration Release
dotnet ef migrations has-pending-model-changes --project src/HorseRacingPrediction.Infrastructure/HorseRacingPrediction.Infrastructure.csproj --no-build --configuration Release
dotnet test HorseRacingPrediction.sln --no-build --configuration Release --collect:"XPlat Code Coverage" --filter 'TestCategory!=External'
dotnet list HorseRacingPrediction.sln package --vulnerable --include-transitive
git diff --check
```

Chromiumが未準備ならbuild後にtest projectの `bin/Release/net10.0/playwright.ps1 install chromium` を実行する。EF空DB適用は実行時に作った専用tempディレクトリのDBへ `dotnet ef database update ... --connection "Data Source=<専用temp絶対パス>/eventstore-ci.db"` を行う。本番接続文字列・既存DBを指定しない。schemaを含む変更では空DBだけでなく旧SQLite testも必須。

公式E2Eは外部アクセス許可と現在のfixture条件を確認し、`JraSiteE2ETests.完了済みRaceResult取得` / `古いRaceResult取得` を含む限定filterで実施する。大量の再収集や無制限並列実行はしない。失敗時は期待ページ・対象identity・安全な診断だけ保存し、未編集ログを公開しない。

## 6. T3: 本番復旧の手順・停止条件

これは実行手順の契約であり、今ターンの実行指示ではない。本番writeは別途明示承認済みの解消taskが担当する。

| Gate | 実行内容 | 必要証拠・進行条件 |
| --- | --- | --- |
| O1 | 対象environment/API/Collector/DB、許可範囲、配備revision、担当を固定 | dirty checkoutを使わず、証拠時刻・旧revision・rollback先を記録 |
| O2 | schema変更有無、API/Collector互換性と配備順を確認 | PR #65 schemaを含む場合backupとrestore手順、running drain条件を確認。配備手段がActionsしかなければ勝手に起動せずblocker提示 |
| O3 | 承認された方式で配備 | API/Collectorのartifact識別子とhealth確認。片側失敗なら復旧対象投入を始めない |
| O4 | canary候補を現在状態で再検証 | 各根本原因の代表1件から開始、初回合計最大5件。同じ入力・revisionで既に失敗した対象の盲目的retryは禁止 |
| O5 | 選択したnotificationだけRecovery、必要時だけ一度resume | `/failure-notifications/recover`の通知ID選択を利用。group全体recover、履歴削除、runnerの`-Recover`は使わない。running対象のleaseを奪わない |
| O6 | 対象の終端と次の独立したtask成功をGETで確認 | request/task/attempt、domain保存、facet、failure lifecycleの対応を確認。要求受付200だけでは合格しない |
| O7 | 観測窓を満たした後だけ拡大・完了判定 | 原因別に同じ承認対象内で段階実行。条件外候補は追加承認/設計へ戻す |

観測窓は当日有効なdispatcher/scanner周期の2回以上かつ、選択taskの実行完了までとする。最初に周期・due・通常実行時間から観測上限を記録する。周期不明やtask完了なしなら観測不足とし、無限待機も即時成功判定もしない。外部待機は製品の待機/定期機構を使う。

次のいずれかで追加投入・再開を停止する: 同原因の即時再停止、unknown整合性例外、誤ったRace/主体への保存、同Resourceの重複実行、API/Collector互換性不一致、広域失敗閾値到達。停止時は原因taskと配備物の証拠を保全し、既存rollback条件に従う。DB schemaを伴う場合にbinaryだけ戻せば安全とは仮定しない。復元・補償に新しい破壊的操作が必要なら利用者確認を要求する。

POST応答がtimeoutした場合、再POST前に同Resourceのrequest/task・failure状態を読む。結果が不明なら再実行しない。これによりtoken切れ・通信切れを二重Recoveryへ変えない。

## 7. データ鮮度の実測と完了の定義

- Card: 既存金曜18:00 JST checkpoint、21:00 criticalを維持。公式に公開された対象開催/Raceの期待集合と、保存Cardの集合を突き合わせる。discovery 0件は100%ではなくunknown。
- Result: 公式発走予定+30分、開催日18:30という既存監視checkpointで対象Raceを照合する。これは実際のレース終了時刻や公式公開からの取得遅延を直接証明しない。公開時刻が取れない場合、公開確認時刻からの遅延上限と明記する。
- 実行時の有効設定が既定値と違う場合は設定値を記録し、今回勝手に閾値を変えない。振替・通常平日も対象で、金曜/週末以外を収集対象外にしない。
- reportは期待件数、保存成功、公式Unavailable、未公開待ち、収集失敗、unknownを分ける。Unavailableを「取得成功」に足さない。保存時刻と観測時刻を区別する。
- Code完了、配備完了、対象回復、観測完了を別々に記録する。金曜/開催日の未到来観測はDependentで残し、通常処理の復旧を先に報告しても全体Implementedとはしない。

## 8. 実行成果物・再開可能性

本統合recordはMainだけが編集。原因別taskは自身の専用recordへ結果を残し、本統合recordへ直接書き戻さない。Mainが証拠を参照してACを更新する。

実行時に必要なものだけ作成する:

- `evidence/baseline.md`: revision/原因/影響/既存task対応。
- `evidence/verification.md`: コマンド、revision、実行時刻、件数、結果、skip/blocker、独立反例。
- `evidence/operations.md`: 許可範囲、対象ID、previewの安全な要約、配備/回復結果、観測窓、rollback条件。
- 原因別専用recordのagent audit: coding委譲時だけ。tokens/model観測不明を推定しない。

task終了・中断前に次の具体操作と担当、依存、未コミット変更、最後の本番操作とその確認結果を記録する。Codex task IDで追跡し、同一原因のtaskを増殖させない。機密資格情報、未編集ログ、接続文字列は成果物に含めない。

## 9. 設計上の残る不確実性

本番停止の直接原因、現在の配備revision、Ownerの不整合原因は、この文書詳細化だけでは確定しない。T1はその証拠gateである。既知修正の統合とテストは具体化したが、未確認の原因まで「これで直る」とは約束しない。

本書で内容を確定した実装は既知契約の統合・不足/回帰修正・テストである。新規identityモデル、停止policy変更、新しいmigration、個別本番applyは必要性と安全設計を確定して別途承認する。その承認gateは今回の変更からの除外を意味しない。原因別に設計を追加する必要が出た場合も、既知で独立した復旧sliceは止めずに進める。

## 10. 改修完了までのclosure gate

対象はT1開始時のbaselineで識別した原因群と、それを解消するために判明した収集経路の関連欠陥。各原因にowner、専用task/record、再現入力、変更/変更不要の根拠、test、配備revision、必要なRecovery、独立した本番結果を対応付ける。単に「taskへ引継ぎ」「原因不明」「承認待ち」と書いた行は閉鎖できない。

T2fは、停止原因・Owner/profile等の未確定枝を、調査→必要設計→承認gate→実装→反例/回帰→T2e→T3aまで追跡する。データが元から取得不能なら、その公式根拠と有限・安全な終端を証明する。偽の正常化や曖昧なID統合は使わない。

T3cはAC1-AC7、全対象子task、元recordの受け入れ条件を照合する。必要な改修/配備/補正/観測のどれかが未完了なら、本変更は未完了のまま維持する。通常収集の一部復旧は途中成果として報告できるが、全体Implementedとはしない。本番アクセス・破壊的操作の承認が必要なら、その具体的blockerを提示し、安全な独立作業を継続する。

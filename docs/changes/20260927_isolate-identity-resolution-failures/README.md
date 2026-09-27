# 同定根拠不足を対象単位で保留し収集を継続する

- Status: Approved
- Change record schema: 2
- Owner: Codex lead
- Created: 2026-09-27
- Updated: 2026-09-27

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | 422契約、typed分類、部分保存診断を実装・ローカル検証済み |
| Verification | In progress | 最終全体1427件成功・既存skip1。PR作成後のLinux CIのみ残る |
| Deployment/operation | Not started | GitHub配備と既存停止解除/再要求は別承認 |

## Context / hypothesis ledger

[本番調査](../20260927_resource-repair-tool/incident-cef9432c77ca6952.md)の3頭はprofile保存後に409。父馬は公式identity付きで既存、後続の父Upsertは名前のみ。`CollectionIdentityResolver.ResolveHorse`は候補1件でも公式identity付きの馬へ名前のみで接続しない。この条件が409になることは既存API統合テストで確認。別馬であると判定したわけではない。

事実: `HttpDataCollectionWriteService.ResolveIdentityAsync`のEnsureSuccessStatusCodeがAPI本文のcodeを捨てる。`CollectionAttemptFailureClassifier`は汎用409をPermanentFailure、既定StopPipelineとする。storeはResourceNotFoundまたはIsolatedなら失敗履歴を残しても全体停止しない。通常のJRA同定不可はResourceNotFoundなので既に非停止。API同定不足だけこの経路から漏れている。

限界: 本番各attemptの正確な呼出先と409本文は保存されていない。全409がこの原因とは断定しない。今回設計は現在の既知のtyped errorだけを識別するため、この証拠欠落を過去エラーの自動再分類で埋めない。

## Goals / decisions

- 内部API `/api/identity/horse` の `HorseIdentityEvidenceRequired` または `AmbiguousHorseIdentity` は409ではなく422と構造化codeを返す。Collectorはこの組合せだけを同定根拠不足/複数候補の型付き例外にする。更新中の旧API互換として同じendpoint/codeの409も認識する。任意のメッセージ文字列による判定をしない。
- 対象taskを失敗・要確認（FailureImpact.Isolated）として永続化し、同じ失敗の自動無限retryをしない。保存済みprofileは保持するがtask全体を成功扱いしない。同じbatchの後続と次batchを継続する。
- 今回は最小の暫定対処とし、同じ親task内の残りの血統/過去レース探索は成功したとしない。未実施を記録し、復旧後の再要求で実施する。これは全体収集を止めないことと区別する。
- エラーにはsafe code、呼出path、子主体名/種別、stageを記録。秘密、header、lease token、任意の応答本文を保存しない。未知/壊れた本文/別endpointの409は従来の安全停止。
- `HorseIdentityConflict`（既存identity/出生などの矛盾）、InvalidHorseSourceIdentity、未知例外、保存データ不整合は今回の非停止化に含めない。通常のJRA候補なし/複数候補の既存非停止も回帰確認する。
- 同定規則を緩めたり、名前だけで候補を採用したり、既存馬のIDを付替えたりしない。公式血統identityをDOMから保持する恒久改善は別設計。全リソース補正ツールも承認待ちを維持する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact / proposed disposition / alternative | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | 汎用409には同定以外の競合も含まれる | trusted endpoint+known codeに限定。全409無視は却下。未知停止を維持 | AC1,3/T1 | 必須 | 最新指示で承認 | Resolved in design |
| C2 | profile保存後に子探索が途中終了する | 部分保存を保持しtaskは失敗/要確認。残探索の未実施を明示。全成功化は却下 | AC2,4/T1 | 推奨 | 最新指示で承認 | Resolved in design |
| C3 | 既存3件の本文なし | 過去エラーを推測で閉じない。配備/再要求は別承認 | AC4/T1 | 必須 | 最新指示で承認 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 根拠不足/複数候補のAPI422がtyped errorとしてworker→completion HTTP→storeに渡り、通知を保持してpipelineを止めない | T1 | 名前のみ父参照+公式identity既存の実API/worker再現、DTO往復/readback | Verified |
| AC2 | 同じbatchの後続taskと次batchが実行できる。対象taskは成功化せず、無限retryなし、誤結合/上書きなし | T1 | batch/dispatcher/store統合、単一・複数候補fixture | Verified |
| AC3 | 未知409、identity矛盾、壊れた本文は安全停止。既存JRA候補なし/複数の非停止動作を維持 | T1 | 分類反例と既存回帰 | Verified |
| AC4 | code/path/子主体/stageと部分保存/未実施を確認できる。機密なし。全関連テスト/format/build/CI成功。本番再開等なし | T1 | 実API transport、ログ検査、workflow同等検証 | Connected |

## Task plan

T1: Lead/high tier。API error契約、分類、worker/storeの部分失敗意味論と統合検証を一体で所有する。write scopeはContractsのtyped error、API identity endpoint、CollectionOperations classifier、Collector identity client/handler、関連API/Collector testと本記録。単独の短い文字列変更ではなく停止境界/契約の判断を含むため主担当保持。State: In progress。AC1–4に対応。依存なし。focused test後に全体検証を行う。

## Documentation updates

`docs/26-collection-platform-design.md`に暫定非停止境界の提案を追記。画面変更は行わず既存失敗詳細を使用するためモック不要。

## Verification record / next action

調査時に `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~HorseProfile_EquivalentOfficialUrlsResolveLegacyId_ButNameOnlyAndDifferentSourceAreRejected` が1件成功。これはguardの証拠であり暫定処置の完成証拠ではない。

承認: 利用者の最新指示に基づき上記の限定的422化と非停止化を実装する。騎手の通常同定不可も既存非停止を確認し、引退ページ探索の恒久改善は別途扱う。本番変更は含めない。

Pre-implementation review (Lead): T1をIn progressとする。依存なし、write scopeにAPI identity endpointとCollectionOperations classifierを含む。契約・安全境界とHTTP/store統合が一体の短い修正のため分割コストを避け主担当が所有する。API/Collectorの関連テストを追加し、`dotnet test ...Api.Tests.csproj --configuration Release`、Collector.Testsの同等コマンド、`dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`、Release build、全体非External testを実行する。未知競合非停止や誤同定が見つかれば局所修正して再検証する。補正ツールの未承認変更は混在させない。

## Checkpoint / failure closure

- API focused 7件、Collector focused 56件成功。実APIの公式identity付き父参照→422→typed exception→worker完了HTTP→store通知を再現。実dispatcher/wake/Lambdaで同じbatchの2番目と次batchを実行し、親失敗の再acquire不可、既存馬ID不変を確認。
- 初回API試験: テストのApi.Contracts using不足でcompile失敗、追加して解消。続く2件失敗は新fixtureの生年月日欠落によるprofile検証409、および名前由来IDへの公式ID昇格禁止が根拠不足codeなのに旧409期待を残したため。fixtureに実データの生年月日を追加、契約どおり422期待へ修正。元の7件コマンドを再実行して全成功。製品の同定guardは変更していない。
- `codegraph sync .`: index未初期化で失敗。索引は作成せず、source/diff/caller検索と実行テストで確認する。graph確認済みとは主張しない。
- 以前の記録との照合: DOM対応の設計§4/AC5は血統8件を保留する方針だった。今回の3件はその8件そのものではなくprofile保存後の新たな参照解決失敗。引退馬/騎手リンクの修正漏れとは異なり、APIの既知同定不足を同じ非停止境界に接続していなかった実装・統合試験の漏れ。`learn-from-implementation-failures`で確認し、既存DDDのtransport traceability gateが既に要求する検証を今回追加する。重複したskillルールは追加しない。
- 騎手候補なし/複数候補の既存handlerを回帰確認。今回騎手の同定規則は変更しない。引退リンク選択は前回のAC2で実装・検証済みであり、今回の内部馬APIエラーとは別経路。
- 次の操作: exact format確認→Release全体build→非External全体test→差分監査/commit→PR/CI。未承認の全resource補正ツールrecordとdocs/20、docs/26のその提案段落は意図的に未コミットのまま保持する。配備・本番再開・旧失敗の再要求はしない。
- 第1回全solution: 1427成功・既存skip1・失敗0。API coverage instrumentでDLLロック警告あり。format検証と最初のbuild/testの実行時間が重なったため、最終検証はformat終了→build終了→testの順に直列実行する。coverage collector未導入の3project（Agents/Collector/Scraping）の警告は既存構成に起因し本修正と無関係。coverageの完全性は主張せず、テスト成否と分けて扱う。
- localhost隔離API2プロセスsmoke成功: 14entries/14owners、read-only preview、cross-process lock、冪等性、durable hold、backup、遅延odds拒否。EF pending modelなし（既存tool8/runtime10の警告は変更対象外）。本番未接続。
- validatorはroot scriptsに存在しないためskill配下の `.codex/skills/document-driven-development/scripts/validate_change_records.py` を実行しissues=0。
- coverage追跡: format/build終了後の直列起動でもAPIのinstrument警告を再現したため、format重複仮説は否定。対象ApiClient.dllをロードしているプロセスを読取検査し、当該テストのdatacollector自身とtesthostであることを確認（PID42504/86868、同一vstest親91688）。本変更ではcollector package/project構成を変えていない。Windows計測上の制約として明示し、テスト結果とLinux CIを独立に確認する。coverageの完全取得は今回の同定非停止ACではなく、既存テスト基盤のフォローアップ（owner: Lead）とする。

## Final local review / remote verification handoff

LeadがAC1–3の統合差分と最終テストを照合。既知2codeのみ422/Isolated、未知競合停止、同定guard不変、過去失敗の自動再分類なし。APIの複数候補は`AmbiguousHorseIdentity`本文まで確認。実handlerの部分保存診断と実API/worker/store/wakeの非停止をそれぞれ検証し、ブラウザー/本番サイト全体の再現とは区別する。

- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: 成功。
- `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release`: 成功、警告0/エラー0。
- `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --collect:"XPlat Code Coverage" --filter "TestCategory!=External" --logger "trx;LogFileName=app-ci.trx"`: 最終再実行1427成功・既存skip1・失敗0。coverage制約は上記の非阻害follow-up。
- `tests/scripts/test-entry-repair-local-host.ps1 -Configuration Release`: 成功。
- change record validator、`git diff --check`、staged差分の対象/生成物/秘密情報検査: 成功。

残項目はAC4/T1のLinux CI終端確認のみ。利用者の「PR後の追加commit禁止」を守り、PR作成時点の本ファイルはApproved/CI待ちとして固定する。CIのrun URL・commit SHA・annotation分類・AC4/T1最終判定は、当該PRの追記を本記録のremote verification continuationとして残し、CI未確認の時点でImplementedとは記載しない。本番配備/停止解除/再要求は別承認。

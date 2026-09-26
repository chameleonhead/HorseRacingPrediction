# 識別ルールの重複を解消し、馬主収集のID不一致を復旧する

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-27
- Updated: 2026-09-27

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | 共通identity/既存ID互換/限定owner復旧を実装。Release buildと全体nonExternal回帰成功 |
| Verification | Externally blocked | AC1–5 Verified。ローカル/Linux CI成功。本番46件の履歴保持・冪等性を確認。別件停止によりAC6のworker成功/後続進捗は未達 |
| Deployment/operation | Externally blocked | API/collector同版配備成功、馬主46件の復旧要求Ready/未実行。未知のjockeyエラーを保持し、再開・別原因修正には追加対応の合意が必要 |

## Context / scope

利用者は馬主エラーの原因と同様の重複実装の調査を依頼し、調査報告後「対応をお願いします」と依頼した。本書は未提示だった既存ID互換・運用復旧条件を明示する承認対象であり、一般的な対応依頼だけでそれらを承認済みとは扱わない。

起点は[取消対応の別件記録](../20260927_race-card-cancellation/evidence.md)。2026-09-27 02:28 JSTの観測でowner-identityのSubjectNotIdentifiedが10件、pipelineは稼働中だった。現在件数や全件の同一原因は未確認。代表の失敗は要求IDと馬主APIが解決するIDの不一致であり、名前の欠落ではない。

対象は馬主のID/別名解決、馬のfallback ID、馬検索名照合、公式馬identity照合、競馬場・レースidentity、およびこれらの呼出経路。関連するdefinition/revisionの重複定義も共通の記述元へ接続する。リポジトリ全体の汎用リファクタリング、新規画面、IDアルゴリズムの全面交換は含めない。

## Hypothesis / evidence ledger

| ID | Claim / fact boundary | Supporting and contradicting evidence / falsification | Result / disposition |
| --- | --- | --- | --- |
| H1 | 馬主の生成ルール不一致が代表障害の原因 | RaceResultBulkのjob側はNormalizeKey、馬主APIはOwnerIdentityContract＋alias mapping。実compiled helperで同じ名前から本番の2つのIDを再現 | 確認済み。job側を共通の解決処理へ接続 |
| H2 | 共通CreateIdへ変更するだけで十分か | BuildOwners/ResolveOwnerIdはOwnerAliasMappingsを優先し、job側は無視。統合後aliasのhashは統合先と異なる | 却下。正規化だけでなくmapping優先の解決を共有 |
| H3 | 馬のfallback生成は経路で異なる | HttpDataCollectionWriteServiceはraw name、bulk APIはCanonicalizeDisplayName後にBuildHorseId。実helperで「マル外 サンプル」「ＡＢＣ」のID相違を再現 | 確認済み。ただし当該表記の本番障害発生は未確認 |
| H4 | 検索/検証で名前・source identityの同値判定が違う | JraNavigator.Subjectsは汎用Normalize、profile validatorはHorse固有正規化。validatorは正規化Horse identityも比較、profile POSTはraw一致 | source上の反例確認。固定snapshotとHTTP保存更新で実装時に反証 |
| H5 | 英日競馬場で同一RaceのIDが分かれる | BuildRaceId(Tokyo)とBuildRaceId(東京)は異なり、resource→IDは日本語へ変換。実helperで異なるUUIDを再現。lease照合にderived/explicit矛盾拒否がある | 確認済み。既存IDを維持する解決が必要。本番該当件数は未確認 |
| H6 | その他のhashがすべて危険な重複か | EntryIdとassignment fingerprintは集中済み。補正manifest/batchのhashは目的が異なる。horse revision fallback3は通常current4取得で上書き | 一律置換を却下。異なる目的のhashと後方互換は保持 |

調査baseは1d961df5。CodeGraphはgitignore stubのみでindex未作成、exploreが利用不可を返したためsource探索を使用。新規indexは作らない。先行read-only explorerはsubject/raceを分担、requested model gpt-6-sol、observed model/tokenは未確認。Mainがsourceとcompiled helperの反例を独立照合し採用。調査によるsource変更なし。

代表例: 「(株)ノルマンディーサラブレッドレーシング」はjob key「株ノルマンディーサラブレッドレーシング」からowner-dbe401ae-0c14-5102-beb3-ac5cad1f7ffb、共通owner正規化後はowner-74ff355a-d012-573c-8f22-f2bcfd342d09。前者の本番GET404、後者は2026-09-26中山1Rの出馬表に対応。現行検証APIの存在確認を無効化する解決はしない。

## Decisions / compatibility

承認記録: AC1〜AC6を観測可能な受け入れ条件として再提示した後、利用者が「お願いします」と明示。C1〜C5の既存ID保持・曖昧拒否・安全境界と、GitHub配備/一意対象限定復旧を含め承認としてExecution Modeへ移行。先行の提案/承認待ち記述は設計経緯であり、この承認を現在状態の正とする。

Pre-implementation: MainがT0完了を確認、T1/T2/T4はRunnable、T5/T6はDependent。T3は既存JraSubjectNameNormalizerのHorse規則と共通JraSourceIdentity.MatchesHorseを凍結した時点でRunnable。MainはAPI/profile保存境界・新共通helperを所有し、workerはNavigator/Parserと専用testのみ。共有buildはMainが調整。承認済み既存IDの非破壊保持と拒否条件を変更しない。

1. 純粋な表記正規化・識別値抽出はContracts/ApiClientの共通契約、DBを必要とする別名/既存ID解決はAPI側の一つのresolverへ集約する。Collectorが別のIDを再計算して上書きする経路を残さない。
2. 馬主は正規化→登録済みalias/統合先→未登録時のみ共通ID生成の順とする。job生成、馬主API、出馬表のOwnerId、補正previewで同じ解決を使う。未登録/矛盾を無条件成功にしない。
3. 馬は有効な公式identityを優先し、ない場合だけ共通canonical nameからfallbackを生成する。既存fallback IDは書換えない。既存のcanonical/legacy候補を照合し、一意かつ名前同値・公式identity/生年月日の矛盾なしなら既存IDを保持する。複数候補、同名別馬、公式identity矛盾は自動統合せず保留とする。公式IDを持つ別馬を名前だけで結合しない。
4. 馬の検索候補、選択、profile検証は同じHorse名同値規則を使用する。緩い部分一致にはしない。公式identityの同値判定は共通JraSourceIdentityを使い、識別値が異なるURLは拒否する。URLの許可host/schemeや生年月日確認は維持し、未知形式を勝手に同一扱いしない。
5. 競馬場は対応済み10場の日本語/英語・大文字小文字を共通canonicalへ変換する。新規ID/resource IDはこの契約を使用。既存レースは日付＋canonical競馬場＋レース番号の一意照合で既存RaceIdを維持し、派生IDとの差だけで別レースを作らない。複数既存候補やmetadata矛盾は保留。任意のexplicit IDを無検証で優先して安全境界を回避しない。
6. 既存HorseId/RaceId/EntryId、events、予想/結果、別名統合の履歴は再採番・一括移行しない。entry IDとassignment fingerprintの共通処理、取消全行保存/Activeのみ予想、木曜番号未確定待ちを非回帰対象とする。
7. definition/revisionはAPI登録、Collector登録、発見/手動/補正投入から同じ記述元を参照する。単なる共通化だけのために全データ再収集はしない。修正版で必要なdefinition revisionは対応テストと一緒に更新し、旧失敗はrevision更新だけで解消扱いにしない。
8. 本番馬主復旧は、元要求名・参照race・alias状態・修正後ID・現在の存在確認から対応が一意に証明できる対象だけ。全failure一括retryや馬/レースの過去ID統合は行わない。previewの入力fingerprint、件数、対象IDとactionを記録し、apply前の再読取で変化があれば中止する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 正規化変更は既存IDを変え得る。H3/H5で再現 | 重複/既存参照破損 | 新規生成を共通化、既存ID一意解決、曖昧保留。過去event/予想を付替えない | AC2/4/T2/T4、既存legacy候補反例 | 推奨 | AC1–6とともに承認 | Resolved in design |
| C2 | 馬主aliasはhashだけでは再現不能 | 統合後ジョブ失敗 | DB resolverを共有し、mapping変化も検証 | AC1/6/T1/T6、統合済みalias反例 | 推奨 | AC1–6とともに承認 | Resolved in design |
| C3 | 同値判定の緩和で別馬や不正URLが通る可能性 | 誤結合/安全性低下 | 部分一致禁止、公式ID・生年月日矛盾拒否、host/scheme制限保持 | AC2/3/4/T2–T5 | 推奨 | AC1–6とともに承認 | Resolved in design |
| C4 | 別件の全障害/全重複を直したとは証明できない | 誤った完了報告 | scope内の全経路を棚卸し、再現済み反例と本番観測を区別。曖昧データは件数/理由を残す | AC5/6/T5/T6 | 推奨 | AC1–6とともに承認 | Resolved in design |
| C5 | 復旧中の実行/alias変更、再試行の重複 | 新旧job混在 | 対象preview、必要時pause/drain、再検証、冪等操作、旧履歴保持。未知failureなら再開しない | AC6/T6、競合/二重apply反例 | 推奨 | AC1–6とともに承認 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 馬主の法人表記/空白/文字幅/英字・登録aliasで、jobと馬主API/出馬表が同一IDへ解決。統合先・元名を保持 | T1,T1D,T5 | 登録→別名統合→bulk request→worker存在確認のHTTP/DB統合test | Verified |
| AC2 | raw/canonicalの馬名でfallback生成が一致し、既存ID/参照を保持。公式ID優先、同名別馬・複数候補・矛盾拒否 | T2,T5 | direct writer/bulk/profile発見のcross-path反例、既存events/read-back比較 | Verified |
| AC3 | 馬検索と保存検証が同じ名前/公式identity規則を使う。等価URL表記のみ許容し、異なる馬/誕生日/不正hostを拒否 | T3,T5 | 固定HTML→navigator/parser→profile HTTP保存更新。正常/拒否双方 | Verified |
| AC4 | 英日/大小文字の競馬場で同一Raceへ解決、既存IDを保持。複数既存Raceは自動結合せず、lease/holdの整合性維持 | T4,T5 | 新規/既存race、補正metadata、lease/odds/手動/履歴経路の統合test | Verified |
| AC5 | 重複生成・照合の全対象呼出しとbootstrap/revisionを棚卸し、旧処理の残存を分類。既存取消/予想/結果/odds、安全停止を維持 | T1–T5,T1D | literal inventory、exact format/Release build/nonExternal全体、empty DB/pending model、別process smoke、Linux CI | Verified |
| AC6 | GitHubでAPI/collector同版配備、previewで一意な馬主対象だけ復旧して成功と後続進捗を確認。曖昧/別原因は証拠付き保留 | T6 | workflow/health、before-after対象一覧、冪等apply、元履歴保持、正常後続batchまたは15分観測 | Connected |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T0 | 設計・互換境界・承認 | Main | Lead | - | 本record/canonical docs | hypothesis/concern/AC照合、validator | 本書、先行反例 | Verified | Lead — identity/persistence decisions | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T1 | 馬主resolver・job接続 | Main | Lead | T0 | API owner解決/bulk/関連tests | AC1、alias/worker統合 | HTTP横断反例と全体回帰成功 | Verified | Lead — DB/統合先/同一APIファイル所有 | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T1D | 馬主の横断HTTP反例test | owner_identity_tests | Cost efficient coding | T1 | tests/HorseRacingPrediction.Api.Tests/SharedOwnerIdentityTests.cs | AC1/5 focused→関連API回帰 | focused 2件、Horse/取消19件成功、Main独立HTTP反例 | Verified | Worker — frozen resolver contract / exclusive test | T1D-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | 馬fallbackの共通生成・既存解決 | Main | Lead | T0 | ApiClient/Contracts/API/Collector identity関連、tests | AC2、cross-path反例 | legacy/event/source/hash衝突反例と全体回帰成功 | Verified | Lead — 公開契約/既存IDとT1共有境界 | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T3 | 検索・profile同値判定接続 | identity_parser_worker | Cost efficient coding | T2 | src/HorseRacingPrediction.Scraping/Jra/Navigation/JraNavigator.Subjects.cs; src/HorseRacingPrediction.Scraping/Jra/Parsing/SubjectProfilePageParser.cs; tests/HorseRacingPrediction.Scraping.Tests/SharedSubjectIdentityTests.cs | AC3、focused→Scraping回帰 | focused 3件/Scraping 334件成功、Main profile HTTP反例 | Verified | 契約凍結後low_cost_coding_worker、MainはAPI保存境界所有 | T3-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T4 | Race/Course共通契約・既存照合 | Main | Lead | T0,T2 | Race resolver/collection lease/hold/API、関連tests | AC4、既存ID/競合反例 | 旧ID/複数候補/lease/hold/discovery反例と全体回帰成功 | Verified | Lead — 参照保持/並行性/セキュリティ | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T5 | revision登録/全caller棚卸し・回帰 | Main | Lead | T1–T4 | definition共通記述/登録/呼出し、統合tests/docs | AC1–5、CI相当gate | [inventory](identity-inventory.md)、ローカル/Linux CI全gate成功 | Verified | Lead — shared bootstrap/統合最終判定 | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T6 | GitHub配備・限定復旧・観測 | Main | Lead | T5 | GitHub、対象preview/applyと操作記録 | AC6、実結果/後続進捗 | 同版配備・46件限定apply/冪等/履歴read-back成功。別件停止によりworker成功/後続進捗未達 | Externally blocked | Lead — 本番/データ保全、別原因対応の合意が必要 | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |

T1Dは馬主のresolver/API契約が凍結し、専用新規testを独立所有できるため追加分割。owner_identity_tests（requested gpt-5.6-luna）が登録→別名→bulk→worker存在確認を独立検証し、Mainが受入を保持する。T3は共通契約凍結後に独立委譲する。テスト候補は馬名の登録記号/全半角、同じCNAMEのURL、別CNAME、複数候補、未知host。最小focused testとScraping回帰の成功をworker完了条件にする。新しい同値規則や保存/移行判断が必要ならMainへ戻す。他タスクは判断と実装の分離を検討したが同じresolver/API境界と既存参照の不変条件を共有するためMainが所有。build/testは共有出力を競合させない。

## Delivery / operation / rollback

実装前に対象producer/consumer（API初期登録、bulk/refresh、direct writer、profile発見、検索、手動登録、履歴、修復、runtime bootstrap）をinventoryへ追加し、各経路をactive/shared/history-compatibilityへ分類する。未分類を残して完了しない。

CI成功後のGitHub push/PR mergeだけで配備する。既存pause/drainと同SHA API/collector配備・backup/health gateを利用。マージ後のbranchへcommit/pushせず、merged local/remote branchを削除。運用結果の追加文書は新しいbranch/PR。

本番は現在のpipeline/実行/失敗を再読取してから対象preview。旧IDへの誤要求とcanonical/alias解決先が一致するものだけをallowlist化する。新対象の存在を確認し、冪等な復旧要求を先に記録して旧誤要求を監査付きで無効化する。旧失敗を成功へ偽装しない。既存の訂正/抑止/復旧機構で表現できなければ対象限定の冪等操作を実装・統合検証する。データ削除や全failure消去はしない。

applyは対象worker停止/排出、alias/参照race/入力snapshotの再検証、exact allowlist、結果記録を条件とし、途中失敗は同じoperation IDで再開可能にする。後続の別対象失敗を黙って対象へ追加しない。曖昧例は一覧で保留し利用者へ提示。必要なpause解除は他の未知障害がない場合だけ。障害時は証拠保全とpause、ID互換を壊す旧版への単純rollbackは行わず前進修正する。

## Documentation updates

- JRA site contract impact: Updated
- `docs/10-domain-design.md`: 共通identityと既存ID保持・曖昧保留の提案を追加。domainの正本。
- `docs/26-collection-platform-design.md`: job生成/存在確認の共通解決、登録定義の一元化と限定復旧の提案を追加。運用/collector正本。
- `docs/27-jra-site-collection-contract.md`: 検索・検証の同値規則共有と、未観測表記をサイト事実と混同しない提案を追加。取得元正本。

## Review gates / next action

Design/task-split: Mainが先行調査のcallerとcompiled反例を照合。ID再生成・別馬自動統合・未知障害一括無視を却下。AC1–6はhappy path/反例/非回帰/本番境界へ対応。T3だけ独立委譲可能、他は既存参照/共有境界の判断をMainに保持。

Concern/agreement: C1–5は上記の境界で設計上解決、利用者がAC1–6とともに承認。現在の本番全対象について一意解決を保証していないため、preview/保留条件をACに含めた。

Pre-implementation: 上記契約を確定して実装開始。Checkpoint: MainがAC1/3の実経路テストとscopeを確認。Final review: 未実施。

次操作: 運用証拠の文書PRをmainへ反映してbranchを削除後、別原因（騎手の公開リンク不足・馬のNoCandidate）の調査/対応設計の追加合意を求める。合意後も未知障害の停止条件を弱めず、原因検証・設計承認を経て再開し、今回の46件の成功と後続batchまたは15分の進捗を確認してT6/AC6を閉じる。再開後の読取: `/api/admin/collection/resources/Owner/JRA/{targetId}/owner-identity` と `/api/admin/collection/pipeline`。未コミットは本record/canonical docsと3つの運用証拠JSONのみ。プロダクションコードの残作業なし。

## Verification / closure items

- Mainの初回API部分回帰: 48件中12失敗。主因は収集resolverを明示IDの手動作成にも適用したこと、および拒否応答のHTTP契約差。明示IDを維持し、bulk拒否はCorePersisted=falseの構造化応答へ戻した。更新後RaceEndpoints/取消/馬主の36件成功。
- 検索worker: focused 3件とScraping非External 334件成功。馬主worker: focused 2件とHorseEndpoints/取消19件成功。Mainの統合ゲートとaudit確定は残る。
- 公式identity付き既存馬を名前だけで結合しない反例、任意旧RaceIdのlease/hold境界、原本URLの不正host、別馬/誕生日矛盾を追加検証中。

### Checkpoint: identity / recovery safety

- MainはAC1〜4の統合差分を照合。手動の明示RaceId、旧HorseId/EntryId、元の登録イベントを保持。表記補正後の旧fallback IDは初回HorseRegisteredの証拠をread-only参照し、再採番しない。生成hashの衝突でも名前同値でなければ拒否する反例を追加。
- [呼出経路・残存識別処理inventory](identity-inventory.md)にAPI/Collector/initializer/修復/引用元/lease/holdと意図的な歴史互換を分類。definition登録を共通記述へ接続。修復理由やseed revision 1を新規取得済みと偽らない。
- 独立reviewer（read-only、requested gpt-6-sol、observed/usage unavailable）がP1: 誤IDの旧式導出証明不足、P2: 最新100task制限を指摘。旧式hashとの一致必須化、元task/requestの直接取得、mutable resource metadataへのfallback禁止、fingerprintへの原証拠追加で修正。reviewerはsource上のclosureを確認。MainのHTTP test 5件（無関係IDの拒否、全件preflight、重複apply、履歴保持、legacy/canonical/official境界）成功。
- worker audits: [T1D-A1](agent-audits/T1D-A1.json)、[T3-A1](agent-audits/T3-A1.json)。独立確認を含むquality/scope gate成功。model/usage/review effort/総費用はruntime非公開で未確認、料金換算やモデル性能順位を推測しない。反復比較標本不足のためpersistent routing変更なし。
- 2026-09-27 06:50 JST、本番は別件の引退騎手リンクエラーで停止中とread-only確認。T6の実worker成功/後続進捗は停止解除が必要だが、未知障害がある間は再開しないという承認済み条件を保持。残るコード・全体検証・GitHub配備・限定復旧の準備を先に完了し、scope外原因の対応権限が必要ならその時点で提示する。完了と偽らず本recordはApprovedを維持。

### Checkpoint: final local gates

- 整形済みsourceに対する全体回帰でAPI 3件のfixture差異を検出。旧revision固定と根拠なし手動馬IDの同名結合を、共通descriptor・証明可能なfallback IDへ修正。履歴のCorePersistedもassert。関連API 23件成功後、Release buildと全体nonExternal回帰を再実行中。
- `dotnet ef migrations has-pending-model-changes --project src/HorseRacingPrediction.Infrastructure/HorseRacingPrediction.Infrastructure.csproj --no-build --configuration Release`: 成功、model差分なし。EF tool 8.0.11/runtime 10.0.11の既存version警告あり。
- 空の一時SQLiteへの`dotnet ef database update`: 全12 migration成功。既存DB・本番データは操作していない。
- `./tests/scripts/test-entry-repair-local-host.ps1 -Configuration Release`: 成功。実localhost API 2 process、14出走/14馬主、preview read-only、cross-process lock、冪等修復、durable hold、backup、遅延odds拒否/current worker許可を確認。
- `codegraph sync .`: 未初期化のため失敗。stubだけの環境を勝手にindexせず、source/literal inventoryと実transport testを証拠とする。graphによる完全性を主張しない。
- 次操作: 最終format/full test結果確認、記録整合とcheckpoint commit、GitHub PR/CI/配備、対象限定owner preview/apply。本番の未知jockeyエラーは保留を維持。作業中のsource/tests/docsはすべて本scope、未コミットは意図的な検証待ち。

最終ローカル結果（2026-09-27 07:14 JST）: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`成功、`dotnet build HorseRacingPrediction.sln --no-restore --configuration Release`成功（警告0/エラー0）、`dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External" --logger "trx;LogFileName=shared-identity-final.trx"`成功（Contracts43/Domain126/Application58/Infrastructure16/ML14/Agents106/Scraping337/Collector356/API317、計1,373成功・0失敗・既存skip1）。最後の編集はtest fixtureのみで、本番sourceを変更していないため先のDB/local host証拠も同一実装に対応。AC1–4はローカル実経路の根拠を取得、AC5のLinux CIとAC6の配備・本番検証は未完了で、AC状態をConnectedに維持する。

Checkpoint review: MainがAC1–4のHTTP/DB・worker・parser経路とAC5棚卸しを統合照合。旧events/ID保持、取消全行/予想対象、lease/hold、未知障害停止を維持。独立review P1/P2は修正済み。変更記録validatorとagent audit validator成功。Git diff check成功、生成物/秘密情報/目的外変更なし。ソース/testの一つの目的は識別契約共通化と同不具合の限定復旧。文書を別commitとし、マージ後branchへ追加commitしない。最終完了判定はAC6未達のため行わない。

### GitHub verification / delivery

- [PR #104](https://github.com/chameleonhead/HorseRacingPrediction/pull/104): docs e8f146ef / source+test fdf54a3d、2026-09-27 07:22 JSTに98b5efb8へmerge。
- [Linux CI 36275708372](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36275708372): 全gate成功（1,373 pass/0 fail/1 skip）。skipは既存`RaceDiscovery_IsRegisteredByNewScheduler`。format/Release/empty DB/model/脆弱性検査/local host/cross-process lockを確認。AC1–5/T1–T5をVerifiedへ照合。
- ancestor・tree一致・clean worktreeを確認し、元branchをdetach後にlocal/remoteとも削除。マージ後の元branchへのcommit/pushなし。運用記録は別branch/PRで追加する。
- [本番配備36276076771](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36276076771)は成功（2026-09-27 07:34 JST）。API IMAGE_TAG/collector image_uriとも98b5efb8、health HTTP200、元のjockey停止保持をworkflow logとAPIで確認。main CI 36276076724も成功。
- 配備前group観測: owner-identity 46件（BA97EBE92E4D55E1）、horse-profile 14件（E56D934CCA551769）、jockey-profile 1件（02D78CBC10B10F67）。名前のみで一括処理せず、配備後previewのproof/fingerprintでowner対象を確定する。

### Production owner recovery (worker completion still pending)

- [実行前preview](owner-recovery-preview.json): 07:35 JST、本番health正常、pipeline paused、Running 0。46件すべて元task/requestの誤ID導出、現在のalias/Owner/参照Raceとの一意照合に成功。対象46/除外0/正しいOwner ID 46をfingerprint付きで保存してからapply。
- [apply結果](owner-recovery-apply.json): 07:36 JST、対象46件だけ新しい復旧task 46件を登録し、誤ID resourceを監査付き抑止。Race/Horse/Entry/予想/結果/eventの付替え・データ削除なし。
- 07:38 JST、同じallowlist/fingerprintを再送して新規作成0、返された46 task IDは初回と完全一致。未解決groupは馬14件・騎手1件だけで、馬主groupは残らない。ただし旧失敗を成功にしたのではなく、旧task/attemptは保持し通知をSupersededにしたもの。
- 停止理由・updatedAtは元の03:08 JSTのjockeyエラーのまま。本番worker成功/後続batchまたは15分進捗というAC6は未達であり、修正配備・復旧要求登録と収集復旧を混同しない。
- [07:39 JST個別検証](owner-recovery-verification.json): 全46旧taskはFailedのまま、元attemptと通知が存在し、通知だけSuperseded。正しい46 Owner resourceそれぞれrequest 1/task 1、ReadyかつattemptCount 0。汎用tasksの先頭200件では新taskが取得できなかったため、resource別の直接read-backへ切替えて全件確認した。対象・件数・ID・旧履歴・二重applyの照合成功。別原因15件のdismiss/retry/削除・pipeline resumeは実施していない。

## Final review / acceptance blocker

MainはAC1–5の実経路、非回帰、Linux CIと独立reviewのclosureを確認しVerified。T1D/T3のworker成果は独立反証と全体回帰を含め受入済み。observed model/usage/costは非公開で未確認、比較根拠のないrouting改善は行わない。今回のfixture・性能・証拠不足は局所closureと反例で修正し、元gateを再実行した。恒久的skill変更が必要な反復プロセス欠陥は本実装からは確認していない。

T6は配備と安全な限定復旧の準備を終えたが、03:08 JSTからの未知jockey障害でworker実行が停止している。停止解除は「未知障害がない場合だけ」という承認済み条件と両立しないため行わない。Mainがblockerを保持し、追加調査/対応設計の合意を求める。AC6をVerified、recordをImplementedにはしない。残課題を別recordへ移して完了扱いもしない。

Incident ledger: 馬主ID不一致の原因/設計/実装/配備は確認済み。46件の置換要求はReady、旧履歴は保持。temporary recoveryおよび収集全体の復旧は未達。残るリスクは未知騎手リンク障害と馬14件の公開検索不成立であり、次の対応後に本recordのAC6へ結果を還元する。

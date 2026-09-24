# 検証checkpoint（2026-09-24）

## ローカル成果

- Main: e036a6b8のmetadata継承patchを最新mainへ統合。
- Main: 既存Current Resultの無操作stageを除去。保存時刻やAppliedRevisionを捏造しない。
- Main: facet Currentでも要求版数未達ならlease上Due。永続履歴は書換えず、実handlerでCard/Result再取得後に新版を保存する。
- 本番・GitHubへのwriteなし。変更禁止基点recordは差分なし。元checkoutのユーザー変更は未変更。

## 実行

| Gate | 結果 |
| --- | --- |
| restore / Release solution build | 成功、警告0/エラー0（最初の統合時点） |
| Store metadata integration | 97 passed（既存+移植3test） |
| Current Result 実保存反例 | 修正前Current→Unavailableで失敗、修正後成功 |
| requested revision反例 | 修正前Due期待/Currentで失敗。修正後成功。実handler連結時のfixture effectiveDate不足を修正し2 focused tests成功 |
| Collector非External | revision修正後298 passed。追加metadata反例後の最終結果は下記追記 |
| 全solution非External | 最初の統合時点: Contracts43/Domain109/Agents106/ML14/Infrastructure15/Application57/Scraping266/Collector297/API274、計1181 passed + API1 skip、失敗0。revision修正後は再実行結果を追記 |
| 外部公式限定E2E | 初回4ケース中3成功、9/21中山は不在で失敗（停止再現）。公式中止を確認し、期待値を開催の実態に修正。9/21阪神、9/22中山、旧中山日付で代替結果を誤取得しない、直近結果、2020結果の5ケースすべて成功 |
| EF model check | pending model changesなし。tool8/runtime10の既存warningあり |
| 空SQLite migration | 専用ランダムtemp pathへ全migration成功。既存DB/production不使用 |
| vulnerable dependencies | 全projectで該当なし |
| format | 最初の統合時点成功。最終差分の結果は下記追記 |

外部E2Eは読取navigationであり、本番domain保存や回復を証明しない。旧日を取得不能とするtestは、中止検知discoveryの実装完了証拠ではない。

## Review

read-only explorer `/root/collection_code_map` requested gpt-6-sol/medium: Current no-op→Unavailableを指摘。Mainが実保存testで独立確認。
read-only reviewer `/root/closure_review` requested gpt-6-sol/high: revision未達のCurrent省略を指摘。Mainが失敗testを確認して修正。observed model/usageは両者とも未取得。coding委譲なし、JSON coding auditなし。
既存規約にある実経路/独立反例gateで今回の欠陥を捕捉しており、新しい恒久routingやskill変更の根拠にはしない。
read-only委譲をaudit validatorの現行分類に合わせてT1-A1/T2e-A1へ記録（write scopeはread-only）。Mainの文書所有は別責務で、子agentが記録を書いたとは扱わない。監査初回のplanned roleと実行routing不整合・scope/metrics不一致を修正してvalid。
再reviewで新testのSystem時刻依存を検出。2026-09-20固定clockを注入し、取得可能期間内かつ結果公開後という前提を決定的にした。最初の9/19固定案は当日startTime不明の正当な待機を踏んだため、翌日というfixture前提へ訂正。

## 最終ローカルcheckpoint

- 最終production差分でRelease全体build成功（警告0/エラー0）。
- 最終production差分でsolution非Externalは1184 passed / 1既存skip / 0 failed。固定時刻test調整後のCollector300件再実行結果は下記追記。
- 修正後も旧日の中山を別日結果として返さない公式E2Eを含む5件成功。
- metadata3移植testに加え、別Resource非継承・別Definition/Resource属性混合防止・Current保存状態保持・新版再収集の反例を追加。
- 本番はpausedのまま、復旧・配備未実施。ローカルcheckpointを全体完了としない。
- 固定時刻調整後のCollector非External: 300 passed / 0 failed。最初に露呈した2つのproduction反例とテストfixtureの失敗は再検証で閉鎖。
- 最終 `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` exit 0、`git diff --check`成功、DDD validator issues=0、agent audit valid。基点record差分なし。
- Final review（全体完了判定ではなく停止時checkpoint）: Mainはlocal diff/test、review反例、未完了ACを照合。既知sliceの実装・検証は通過。T2fの追加契約とT3の認証/操作権限が真正な外部gateのため全体Approvedのまま。未解決原因を除外していない。

## 未完了・再開

1. AWS `aws login` の人による再認証後、get-functionをread-onlyで実行し配備revision照合。現在は認証期限切れ。
2. 中止開催の追加設計は承認・ローカル実装済み（下記）。本番反映と対象限定再実行の証拠を追加する。
3. T1c/T2fの残るprofile/Ownerは入力証拠と現行preflight/repair設計を照合し、曖昧補正は追加承認。
4. T3は配備方式・対象ID・backup/schema/rollback・限定Recovery/resumeの操作許可を確定後に実行。Actions起動禁止を維持。
5. 本番対象終端と独立後続成功、2周期以上、金曜/結果鮮度観測が完了するまで全体Approvedを維持。

未コミット: Store、race handler、StoreTests、direct handler tests、公式E2E tests、本change record配下。worktree基準ff95b224。他の作業ファイルは含めない。

## 2026-09-24 追加承認後の中止開催slice

- 公式日別model/parser/navigatorとdiscoveryを接続。Card→Result fallback内の例外も外側で扱い、公式証拠が一致する中止だけ除外。未知失敗の停止分類は維持。
- 日付付きh1以外にロゴh1がある実サイトで初回試験失敗。対象番組h1選択に修正し、ロゴ・誤表示日fixtureと実サイトで再合格。
- read-only explorer（既存T1-A1 continuation）が月間ニュース内courseの候補混入、caption/table局所境界を確認。Mainが公開HTMLと実経路試験で独立確認。内部JSONを本番契約に採用していない。
- read-only reviewer（既存T2e-A1 continuation）が後続失敗時の中止証拠消失を指摘。既存failure classifierで分類を変えず証拠併記し、`CancelledThenUnknownMeeting_PreservesFailureAndEarlierOfficialEvidence`と再reviewで閉鎖。実行キャンセルの途中証拠保証は既存executorの範囲のまま。
- `dotnet test HorseRacingPrediction.sln --filter 'TestCategory!=External' -c Release -v quiet`: **1198 passed / 1 existing skip / 0 failed**（Collector307、Scraping273、API274+1skip、その他344）。
- 最終meeting count更新後 `dotnet test tests/HorseRacingPrediction.Collector.Tests -c Release --filter 'FullyQualifiedName~JraRaceDiscoveryCollectionHandlerTests' -v quiet`: **24 passed**、うち実サイト統合1件。9/21中山requestなし、阪神12件を実際のschedule/navigator/parser/handler経路で確認。
- 最終fixture更新後 `dotnet test tests/HorseRacingPrediction.Scraping.Tests --filter 'FullyQualifiedName~MeetingCancellation' -c Release -v quiet`: **8 passed**、うち公式実サイト1件（中山旧日/阪神/代替日）。
- AWS get-functionのread-only再照合は引き続き`aws login`要求で失敗。本番配備revision未取得、deploy/Recovery/resume/補正は未実行。T3/AC3/AC6/AC7をVerifiedにしない。
- 新規未コミット: JraMeetingCancellation.cs、JraMeetingCancellationParser.cs、JraNavigator.MeetingCancellation.cs、対応parser tests。追加既存差分: IJraNavigator、discovery tests、FakeJraSessionInfra、docs/27。残るprofile/Ownerは既存T1c/T2fで証拠・承認待ち。基点recordは変更なし。
- 最終Release solution buildは警告0/エラー0、format verify exit0、diff check成功、DDD issues=0、agent audit valid。read-only review continuation回数をJSONとTask planで3へ整合。追加コードの経路はsource/実サイトで検証し、未作成CodeGraph indexの結果を主張しない。
- Main final checkpoint: 承認済み中止sliceの局所完了と、親全体の未完了を分離。T2f残存/本番は真正な外部gate、T2e次sliceはDependent。コミット・PR・deploy・履歴削除なし。次はAWS再認証後のread-only版数照合と限定操作の承認。

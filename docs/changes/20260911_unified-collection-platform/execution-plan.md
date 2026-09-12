# Execution plan

This is the durable continuation plan for the approved unified collection platform. A completed row or commit is a checkpoint, not a stopping condition. After each checkpoint, execute every newly runnable row.

| ID | Task | Depends on | Write scope | Verification | State |
|---|---|---|---|---|---|
| P1 | Replace `EnsureCreated` with data-preserving schema migration and define deployment ownership | — | CollectionPlatform persistence | Existing-v1 DB upgrade test; concurrent store test | Completed |
| L1 | Complete candidate-by-candidate URL fallback and page identity validation | — | Collector/Scraping handlers | Direct invalid→next candidate→discovery tests | Completed |
| I1 | Create replacement SQS/DLQ alongside legacy resources and encode post-smoke deletion | — | Terraform/cutover artifacts | `terraform validate/plan` where available; static contract test | Completed (static validation; Terraform CLI unavailable) |
| O1 | Implement new DLQ reconciliation, retry classification, pause/cancel/watchdog and failure notification | P1 | CollectionPlatform store/API | API→state failure/retry tests | Completed |
| D1 | Complete Discovery API→outbox→SQS→Lambda→child Request integration test | L1, P1 | API/Collector integration tests | Full transport-boundary test | Completed |
| S1 | Implement Horse/Jockey/Trainer handlers and RaceCard reference discovery | D1 | Collector/Scraping/API client | RaceCard→three Resource requests→profile write | Completed |
| S2 | Implement Horse→Trainer/sire/dam discovery with cycle and priority guards | S1 | Collector collection handlers | Cyclic graph test | Completed |
| B1 | Implement year/month Batch, staged result discovery, restart, and hole projection | D1, P1 | CollectionPlatform/API/Collector | Partial failure then later batch and hole query | Completed |
| A1 | Implement append-only OddsSnapshot domain write, parser, handler, and repeated schedule | D1, P1 | Domain/Application/Scraping/Collector | Multiple observed-at snapshots retained | Completed |
| R1 | Finish revision impact preview/progress and recollection expansion | P1 | CollectionPlatform/API | affected/completed/pending/failed test | Completed |
| M1 | Make bulk/manual operations previewed, transactional, and condition based | P1, R1 | CollectionPlatform/API | validation failure produces zero requests | Completed |
| U1 | Replace legacy admin job UI with Resource/State/Task/Attempt/Location/Batch views | M1, O1 | Blazor/API client/tests | component and browser tests | Completed |
| X1 | Separate PredictionExecution from legacy collection processing store | P1 | prediction scheduling/persistence | predictor regression suite | Completed |
| X2 | Delete legacy collection store/runner/scheduler/endpoints/UI/config/tests | O1, B1, S2, A1, U1, X1 | repository-wide | CodeGraph/`rg` legacy production callers = 0 | Completed |
| C1 | Implement idempotent dry-run/execute initialization from Domain Data/source citations | P1, B1 | initialization tooling | repeated execute changes nothing | Completed |
| C2 | Rehearse new queue connection, initialization, smoke, rollback-before-delete, legacy DB/queue deletion | I1, X2, C1 | isolated deployment | recorded rehearsal evidence | Local tooling rehearsal completed; isolated Terraform rehearsal and production execution remain operational work |
| V1 | Run solution tests and end-to-end production-path acceptance matrix | all | tests/docs | every locally verifiable matrix row Verified | Completed |
| U2 | Add server-side CollectionState browsing and complete error-aware task paging/counts | U1 | Store/API/list UI/tests | >1,000 resources and cross-page error filter tests | Completed |
| U3 | Make grouped failure recovery safe beyond 1,000 notifications with preview/confirmation | U2 | Store/API/operations UI/tests | 1,001-item grouped recovery without loss or duplicate active tasks | Completed |
| U4 | Complete Revision registration workflow: scope input, preview, apply, recollect | R1 | API client/operations UI/tests | All four scope forms and preview-before-apply component/API tests | Completed |
| U5 | Add Backfill batch detail, holes, links, and hole-only recovery | B1, U2 | Store/API/Backfill UI/tests | partial batch exposes and recovers concrete holes | Completed |
| U6 | Page Request/Task/Attempt detail histories and expose safe domain links | U2 | Store/API/detail UI/tests | large repeated-Odds history and identity-mapped navigation tests | Completed |
| U7 | Consolidate collection dashboard counts, add lightweight operations auto-refresh, preserve tab state | U2 | projection/API/UI/tests | one refresh contract, no full loading replacement, URL restoration | Completed |
| U8 | Standardize local launch working directory and absolute data paths | — | local scripts/config/docs/tests | root/project launch resolve the same DB and queue paths | Implemented; launch verification remains in U9 |
| U9 | Complete authenticated desktop/narrow browser scenario and accessibility verification | U3-U8 | browser tests/change record | recorded normal/empty/error/large viewport evidence | Completed |
| U10 | Execute isolated Terraform/cutover rehearsal, then production cutover and legacy deletion | C2, U9 | deployment/AWS/runbook | plan, smoke, rollback rehearsal, approved production deletion evidence | Externally blocked: AWS environment and production cutover window |
| U11 | Persist candidate-by-candidate ResourceLocation outcomes through the production worker path | L1, O1 | Collector/API/CollectionPlatform/tests | stale candidate becomes Suspect, transient failure remains usable, successful fallback becomes Active | Completed |
| U12 | Make Backfill hole recovery converge on the latest Resource/Definition outcome | U5 | CollectionPlatform/API/tests | failed hole disappears after successful recovery and remains visible after failed recovery | Completed |
| U13 | Move detail histories to database paging and make latest-task metadata page independent | U6 | CollectionPlatform/API/detail UI/tests | large histories do not materialize fully; totals, tabs and actions remain correct on later pages | Completed |
| U14 | Add URL-only identification entry and reject unidentified URLs without anonymous tasks | L1, U1 | Scraping/API/admin UI/tests | known page creates a normal request; unknown page creates no Resource/Task and returns an actionable result | Completed |
| U15 | Generalize append-only OddsSnapshot from win-only entries to market/selection/value observations | A1 | Domain/Application/Scraping/API/Collector/tests | multiple markets and repeated equal values are retained with source-compatible reads | Completed; JRA parser currently emits the available Win market |
| U16 | Exercise abnormal input, partial failure, tampered worker completion, restart and paging boundaries | U11-U15 | API/Collector/CollectionPlatform/UI/tests | malformed input creates no work; transient state remains retryable; foreign/duplicate outcomes cannot corrupt locations; out-of-range paging and recovery converge | Completed |
| U17 | Harden production SQS/Lambda transport failures and deployment invariants | U16,I1 | Lambda/Terraform/runbook/tests | duplicate delivery remains idempotent; unsupported messages execute no work; transport failures retry before DLQ; timeout/redrive/alarms are contract-tested | Completed (static/local; AWS apply remains U10) |
| U18 | Complete collection status tab accessibility, aggregate counts, and non-destructive refresh | U17 | Store/API/Blazor/tests | real results are inside the selected Fluent tab panel; one count request; refresh preserves content with status; narrow direct-link selection remains operable | Completed |
| U19 | Unify race and collection list navigation with standard Fluent tabs | U18 | Blazor/CSS/component tests/browser | both pages use the same FluentTabs active indicator and shared host class; custom race periods remain truthfully represented; URL and panel selection stay synchronized | Completed |

## Parallelization rules

- Run all rows whose dependencies are satisfied when their write scopes do not overlap.
- The main agent owns integration, conflict resolution, acceptance-matrix updates, commits, and final verification.
- A failed verification returns the row to Running; it does not stop unrelated runnable rows.
- Destructive AWS/DB operations are limited to the approved cutover sequence and are not executed during implementation or rehearsal against production.
- 2026-09-12: U2-U5 を実装した。State と task error filter は DB filtering 後に server-side paging し、1,001件の障害復旧要求を受理できる上限と確認Dialog、Revision の4 scope入力→preview→apply→recollect、Backfill独立詳細とhole-only recoveryを追加した。Store/API/component対象15件とAPI全115件（外部依存1件skip）が成功した。
- 2026-09-12: U8 の相対SQLite/collection state pathをAPI content root基準の絶対pathへ正規化した。起動ディレクトリ差異の実ブラウザー確認はU9で行う。
- 2026-09-12: U6 はResource詳細のRequest/Task/Attempt履歴を共通25件pageへ変更し、30件時のpage 2をStore testで検証した。Horse/Jockey/Trainerは収集詳細から安全な業務詳細routeへ遷移できる。
- 2026-09-12: U7 はoperations dashboardを単一HTTP queryへ集約し、30秒の部分更新、`?tab=`によるtab復元を追加した。認証済みdesktop browserで一覧、運用画面、Revision tabとURL更新を確認した。Revision formのfield hostがgrid上で分離してlabel/inputがずれる問題を自己レビューで発見し、field wrapperで修正した。狭幅の実ブラウザー確認は継続する。
- 2026-09-12: U9 は認証済み390x844 viewportで収集一覧、収集依頼Dialog、運用Revision formを確認した。主要actionとfilterは縦積みされ、情報は欠落しない。初回観察でtabのmin-contentによりpage全体へ横scrollが発生したため、共通detail tabsのoverflowを局所化して再確認し、page横scrollが消えたことを確認した。Dialog dismissにはtooltipを追加した。viewport overrideは検証後に解除した。
- 2026-09-12: 完了後レビューで U11〜U15 を検出した。各項目は失敗再現テストを先に追加し、Store単体だけでなく実際のCollector/API/UI経路を検証する。U10はAWS認証と本番切替時間帯を要するため引き続き外部blockerとし、ローカルで実行可能なU11〜U15を並列実装する。
- 2026-09-12: U11〜U15を実装した。候補URL outcomeはCollectorから完了API/Storeへ同一transactionで伝搬し、恒久不整合だけをSuspect、一時障害を状態維持、成功をActiveとして記録する。Backfill holeは失敗後の同一Resource+Definition成功で解消する。詳細履歴は3種類をDB側で独立page化し、総件数とLatestTaskをpage外metadataにした。URL単独入口はJRA出馬表/結果URLを通常requestへ同定し、同定不能を422で拒否する。Odds snapshotはmarket/selection/value観測を後方互換で保持し、JRA単勝も同形式へ写像する。
- 2026-09-12: Release全体buildは警告0・エラー0。solution testはContracts 38、Domain 96、MachineLearning 14、Infrastructure 11、Agents 107、Application 56、Collector 95、Api 124、Scraping 218が成功し、外部依存2件のみskip、失敗0だった。追加修正後の対象API/UI 21件、Store/Worker/handler 37件も成功した。認証済みブラウザーで収集依頼DialogのURL入口、未入力error、一覧から独立詳細page、履歴tabの総件数表示を確認した。
- 2026-09-12: 利用者指示によりU16を追加した。URL parserの不正/重複query、worker outcomeの改ざん・重複、通信/ページ不整合、Backfill復旧競合、履歴範囲外page、Odds validation/後方互換を独立した異常系テスト群として実行し、検出した欠陥を同じ項目内で修正する。
- 2026-09-12: U16で、重複CNAMEが例外化する入力経路、未捕捉429/5xx/timeoutがPermanentFailureになる分類誤り、矛盾/foreign Location outcomeを部分適用できる経路、極大pageのoffset overflow、再失敗したBackfill recoveryを同じbatch IDが妨げる問題、null Odds payloadが500になる問題を検出して修正した。不正URLは422かつResource/Request/Task 0件、Location completionは全体事前検証と100件上限、transientはretryable、履歴は安定tie-breakと飽和offset、復旧requestは実行単位ID、Oddsは400 ValidationProblemとした。追加レビューでローカルExecutorがcancel済みtokenで完了保存していた経路も検出し、独立tokenでretryable結果を永続化した。最終Release buildは警告0・エラー0、API 161件成功/外部依存1件skip、Collector 110件成功/失敗0だった。
- 2026-09-12: U17で本番SQS/Lambda境界を監査した。API停止やLambda初期化失敗を初回受信だけでDLQへ送っていたmaxReceiveCount=1を3へ変更し、通知にcontractVersionを追加した。旧形式・未知version・空TaskId・不正generationはAPI処理せずpoison messageとして扱う。SQSトリガーには無効なLambda async invoke設定と過剰なDLQ送信権限を削除し、Throttles alarm、900秒timeout対5400秒visibility、4日source対14日DLQ retention、batch size 1/partial responseをstatic contract testで固定した。API 162件成功/外部依存1件skip、Collector 112件成功、Release build警告0・エラー0。AWS apply/実redrive rehearsalはU10に残る。
- 2026-09-12: U18でFluentTabsの空tabpanelを解消し、選択中パネルがfilter・更新状態・障害summary・一覧・paginationを実際に所有する構造へ変更した。5回の逐次task searchをstatus groupによる単一`task-view-counts` APIへ集約し、初回以外は既存パネルを残してFluentProgressRing付き`role=status`を表示する。bUnit/endpoint対象16件が成功し、実ブラウザーのaccessibility treeで待機中tabpanel配下にfilterと4件の一覧があること、狭幅で`?view=all`を直接開いて後方タブが選択表示されることを確認した。
- 2026-09-12: U19でレース一覧の独自button tabを廃止し、収集管理と同じ標準`FluentTabs`、active indicator、共通`page-view-tabs` host classへ統一した。既定の直近30日や手入力期間は標準presetを誤選択せず動的な「指定期間」で表し、選択tabpanelがfilter・一覧・paginationを所有する。bUnitでラベル構成、panel所有、今日presetのURL同期を検証し、実ブラウザーで`/races?page=1`の「すべて」と`/jobs`の「要対応」が同じindicator表現であること、各tabpanel内に実コンテンツがあることを確認した。

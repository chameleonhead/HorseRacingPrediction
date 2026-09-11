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
| U6 | Page Request/Task/Attempt detail histories and expose safe domain links | U2 | Store/API/detail UI/tests | large repeated-Odds history and identity-mapped navigation tests | Dependent on U2 |
| U7 | Consolidate collection dashboard counts, add lightweight operations auto-refresh, preserve tab state | U2 | projection/API/UI/tests | one refresh contract, no full loading replacement, URL restoration | Dependent on U2 |
| U8 | Standardize local launch working directory and absolute data paths | — | local scripts/config/docs/tests | root/project launch resolve the same DB and queue paths | Implemented; launch verification remains in U9 |
| U9 | Complete authenticated desktop/narrow browser scenario and accessibility verification | U3-U8 | browser tests/change record | recorded normal/empty/error/large viewport evidence | Dependent on U3-U8 |
| U10 | Execute isolated Terraform/cutover rehearsal, then production cutover and legacy deletion | C2, U9 | deployment/AWS/runbook | plan, smoke, rollback rehearsal, approved production deletion evidence | Externally blocked: AWS environment and production cutover window |

## Parallelization rules

- Run all rows whose dependencies are satisfied when their write scopes do not overlap.
- The main agent owns integration, conflict resolution, acceptance-matrix updates, commits, and final verification.
- A failed verification returns the row to Running; it does not stop unrelated runnable rows.
- Destructive AWS/DB operations are limited to the approved cutover sequence and are not executed during implementation or rehearsal against production.
- 2026-09-12: U2-U5 を実装した。State と task error filter は DB filtering 後に server-side paging し、1,001件の障害復旧要求を受理できる上限と確認Dialog、Revision の4 scope入力→preview→apply→recollect、Backfill独立詳細とhole-only recoveryを追加した。Store/API/component対象15件とAPI全115件（外部依存1件skip）が成功した。
- 2026-09-12: U8 の相対SQLite/collection state pathをAPI content root基準の絶対pathへ正規化した。起動ディレクトリ差異の実ブラウザー確認はU9で行う。

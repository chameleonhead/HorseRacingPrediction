# Execution plan

This is the durable continuation plan for the approved unified collection platform. A completed row or commit is a checkpoint, not a stopping condition. After each checkpoint, execute every newly runnable row.

| ID | Task | Depends on | Write scope | Verification | State |
|---|---|---|---|---|---|
| P1 | Replace `EnsureCreated` with data-preserving schema migration and define deployment ownership | — | CollectionPlatform persistence | Existing-v1 DB upgrade test; concurrent store test | Completed |
| L1 | Complete candidate-by-candidate URL fallback and page identity validation | — | Collector/Scraping handlers | Direct invalid→next candidate→discovery tests | Completed |
| I1 | Create replacement SQS/DLQ alongside legacy resources and encode post-smoke deletion | — | Terraform/cutover artifacts | `terraform validate/plan` where available; static contract test | Completed (static validation; Terraform CLI unavailable) |
| O1 | Implement new DLQ reconciliation, retry classification, pause/cancel/watchdog and failure notification | P1 | CollectionPlatform store/API | API→state failure/retry tests | Running |
| D1 | Complete Discovery API→outbox→SQS→Lambda→child Request integration test | L1, P1 | API/Collector integration tests | Full transport-boundary test | Running |
| S1 | Implement Horse/Jockey/Trainer handlers and RaceCard reference discovery | D1 | Collector/Scraping/API client | RaceCard→three Resource requests→profile write | Queued |
| S2 | Implement Horse→Trainer/sire/dam discovery with cycle and priority guards | S1 | Collector collection handlers | Cyclic graph test | Queued |
| B1 | Implement year/month Batch, staged result discovery, restart, and hole projection | D1, P1 | CollectionPlatform/API/Collector | Partial failure then later batch and hole query | Queued |
| A1 | Implement append-only OddsSnapshot domain write, parser, handler, and repeated schedule | D1, P1 | Domain/Application/Scraping/Collector | Multiple observed-at snapshots retained | Queued |
| R1 | Finish revision impact preview/progress and recollection expansion | P1 | CollectionPlatform/API | affected/completed/pending/failed test | Queued |
| M1 | Make bulk/manual operations previewed, transactional, and condition based | P1, R1 | CollectionPlatform/API | validation failure produces zero requests | Queued |
| U1 | Replace legacy admin job UI with Resource/State/Task/Attempt/Location/Batch views | M1, O1 | Blazor/API client/tests | component and browser tests | Queued |
| X1 | Separate PredictionExecution from legacy collection processing store | P1 | prediction scheduling/persistence | predictor regression suite | Running |
| X2 | Delete legacy collection store/runner/scheduler/endpoints/UI/config/tests | O1, B1, S2, A1, U1, X1 | repository-wide | CodeGraph/`rg` legacy production callers = 0 | Queued |
| C1 | Implement idempotent dry-run/execute initialization from Domain Data/source citations | P1, B1 | initialization tooling | repeated execute changes nothing | Queued |
| C2 | Rehearse new queue connection, initialization, smoke, rollback-before-delete, legacy DB/queue deletion | I1, X2, C1 | isolated deployment | recorded rehearsal evidence | Queued |
| V1 | Run solution tests and end-to-end production-path acceptance matrix | all | tests/docs | every matrix row Verified | Queued |

## Parallelization rules

- Run all rows whose dependencies are satisfied when their write scopes do not overlap.
- The main agent owns integration, conflict resolution, acceptance-matrix updates, commits, and final verification.
- A failed verification returns the row to Running; it does not stop unrelated runnable rows.
- Destructive AWS/DB operations are limited to the approved cutover sequence and are not executed during implementation or rehearsal against production.

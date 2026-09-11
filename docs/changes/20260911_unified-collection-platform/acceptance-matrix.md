# Acceptance-criterion matrix

Status meanings: **Not started** has no connected production path; **Connected** has a production path but lacks required end-to-end evidence; **Verified** has production-path tests and recorded operational evidence.

| Capability | Status | Evidence / blocker |
|---|---|---|
| Resource/Definition/Revision/State/Request/Task/Attempt persistence | Verified | Versioned migration, legacy baseline, restart restoration, WAL and concurrent initialization are covered by store and startup tests. |
| Active duplicate prevention and repeated same-revision collection | Verified | Transactional active-task uniqueness and repeated terminal tasks at the same revision are covered by store tests. |
| Revision impact scopes | Verified | Typed preview/apply/expand/progress covers all four scopes, preserves non-affected states, queues follow-up after an older active revision, and rejects arbitrary executable selectors |
| SQS notification and Lambda acquire/complete | Verified | Production API/outbox JSON contract and worker acquire/complete endpoints are exercised through the real dispatcher and worker client |
| Timeout, retry, lease expiry, duplicate delivery | Verified | Independent timeout reporting, periodic lease recovery, generation-safe duplicate delivery and DLQ reconciliation are covered by transport/store tests. |
| Direct URL and ResourceLocation fallback | Verified | Explicit/stored candidates reach the lease; handlers validate page type/Race ID, continue after wrong/failed candidates, preserve cancellation, fall back to Discovery, and report the successful URL for verification |
| Race discovery to RaceCard/Result requests | Verified | Admin request, outbox, SQS JSON boundary, worker lease, real discovery handler, and RaceCard/Result child requests are covered by one production-path integration test |
| Horse/Jockey/Trainer discovery and refresh | Verified | RaceCard expands all three types; Horse profile expands trainer/sire/dam with depth, ancestry, duplicate and priority guards; profile handlers use the normal request path |
| Backfill batch and hole recovery | Verified | Persisted year/month batches expand date Discovery without pre-enumerating races, resume after restart, allow later batches after partial failure, and project concrete holes |
| Dynamic schedule and repeated observations | Verified | Policy/due scheduler, active-request flood protection, repeated Odds snapshots, Realtime-first dispatch and four-item Background starvation bound are tested. |
| RaceOdds snapshots | Verified | Parser, direct/fallback handler, configurable near-start schedule, domain event and append-only read model preserve repeated observed-at snapshots |
| Manual/bulk operations and projections | Verified | Seven typed selectors, preview drift rejection, transactional rollback, server-side filtering/paging, grouped failure recovery, Backfill/Revision operations, request/task detail history and the replacement Blazor UI are tested. |
| Pause/cancel/watchdog/DLQ/failure notification | Verified | New store persists controls and notifications; API services test generation-safe DLQ reconciliation, retry/backoff, cancellation, heartbeat, stalled dispatch and expired lease recovery |
| Legacy collector removal and Predictor separation | Verified | PredictionExecution uses a dedicated API-owned SQLite schedule with token leases/restart recovery; legacy collection API/UI/source/config references are removed. |
| New queue cutover and old queue/data deletion | Connected | Terraform keeps old/new SQS and DLQ side by side, gates activation and legacy deletion, and provides a smoke/rollback runbook; an isolated Terraform plan and rehearsal remain |

No capability is marked **Verified** until the real enforcement layer and at least one failure/restart path are exercised. This matrix is updated at every implementation checkpoint.

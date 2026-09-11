# Acceptance-criterion matrix

Status meanings: **Not started** has no connected production path; **Connected** has a production path but lacks required end-to-end evidence; **Verified** has production-path tests and recorded operational evidence.

| Capability | Status | Evidence / blocker |
|---|---|---|
| Resource/Definition/Revision/State/Request/Task/Attempt persistence | Connected | Versioned schema migration, legacy-schema baseline, newer/incomplete-schema rejection, WAL and serialized startup are tested; deployment rehearsal remains |
| Active duplicate prevention and repeated same-revision collection | Connected | Store tests pass, including concurrent store initialization; multi-process task contention remains part of final E2E verification |
| Revision impact scopes | Connected | Store tests pass; progress/operation path incomplete |
| SQS notification and Lambda acquire/complete | Connected | Production code is wired; no transport-boundary integration test |
| Timeout, retry, lease expiry, duplicate delivery | Connected | Timeout reports retryable completion to the new API with an independent deadline; periodic expiry recovery and transport tests exist, but duplicate SQS delivery/DLQ remains unverified |
| Direct URL and ResourceLocation fallback | Verified | Explicit/stored candidates reach the lease; handlers validate page type/Race ID, continue after wrong/failed candidates, preserve cancellation, fall back to Discovery, and report the successful URL for verification |
| Race discovery to RaceCard/Result requests | Connected | Calendar and actual race-list pages expand directly to new API requests without legacy job production; full API/SQS round-trip remains unverified |
| Horse/Jockey/Trainer discovery and refresh | Not started | Definitions only; handlers and reference expansion absent |
| Backfill batch and hole recovery | Not started | Legacy implementation remains |
| Dynamic schedule and repeated observations | Connected | Policy/due scheduler exist and the actual outbox dispatcher now enforces Realtime-first with a four-item starvation bound; request-flood protection remains |
| RaceOdds snapshots | Not started | Definition only; parser/domain snapshot/handler absent |
| Manual/bulk operations and projections | Connected | Basic APIs exist; preview, transactional expansion, condition selectors, UI absent |
| Pause/cancel/watchdog/DLQ/failure notification | Not started | Runtime path still references or removed legacy services |
| Legacy collector removal and Predictor separation | Not started | Collector runtime no longer registers legacy planning/execution services, but API/UI and shared old source remain; Predictor separation remains |
| New queue cutover and old queue/data deletion | Connected | Terraform keeps old/new SQS and DLQ side by side, gates activation and legacy deletion, and provides a smoke/rollback runbook; an isolated Terraform plan and rehearsal remain |

No capability is marked **Verified** until the real enforcement layer and at least one failure/restart path are exercised. This matrix is updated at every implementation checkpoint.

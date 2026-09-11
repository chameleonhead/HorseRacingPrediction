# Acceptance-criterion matrix

Status meanings: **Not started** has no connected production path; **Connected** has a production path but lacks required end-to-end evidence; **Verified** has production-path tests and recorded operational evidence.

| Capability | Status | Evidence / blocker |
|---|---|---|
| Resource/Definition/Revision/State/Request/Task/Attempt persistence | Connected | SQLite schema/store exists; migrations and multi-instance ownership unresolved |
| Active duplicate prevention and repeated same-revision collection | Connected | Store tests pass; cross-process contention is not verified |
| Revision impact scopes | Connected | Store tests pass; progress/operation path incomplete |
| SQS notification and Lambda acquire/complete | Connected | Production code is wired; no transport-boundary integration test |
| Timeout, retry, lease expiry, duplicate delivery | Connected | Timeout reports retryable completion to the new API with an independent deadline; periodic expiry recovery and transport tests exist, but duplicate SQS delivery/DLQ remains unverified |
| Direct URL and ResourceLocation fallback | Not started | Data is persisted but not supplied to or used by handlers |
| Race discovery to RaceCard/Result requests | Not started | Current handler invokes the legacy job producer |
| Horse/Jockey/Trainer discovery and refresh | Not started | Definitions only; handlers and reference expansion absent |
| Backfill batch and hole recovery | Not started | Legacy implementation remains |
| Dynamic schedule and repeated observations | Connected | Policy and due-state scheduler exist; actual dispatcher ordering and request-flood protection absent |
| RaceOdds snapshots | Not started | Definition only; parser/domain snapshot/handler absent |
| Manual/bulk operations and projections | Connected | Basic APIs exist; preview, transactional expansion, condition selectors, UI absent |
| Pause/cancel/watchdog/DLQ/failure notification | Not started | Runtime path still references or removed legacy services |
| Legacy collector removal and Predictor separation | Not started | Legacy production callers and runtime registrations remain |
| New queue cutover and old queue/data deletion | Not started | Terraform rename does not implement smoke-gated deletion |

No capability is marked **Verified** until the real enforcement layer and at least one failure/restart path are exercised. This matrix is updated at every implementation checkpoint.

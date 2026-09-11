# Local queue soak test (2026-09-11)

## Scope

- Duration: 120.59 minutes (120 samples)
- Route: API outbox -> persistent local SQLite queue -> persistent Collector -> collection worker -> API domain writes
- External dependencies: none (SQS semantics were represented by the local visibility-timeout queue)
- Workload: current discovery plus one historical backfill batch

## Result

The queue execution path remained alive for the full run and recorded no local queue delivery failures or unhandled API failures. The run is nevertheless classified as **failed for business-flow acceptance**, because it exposed an incorrect first-write path and task churn.

| Observation | Result |
|---|---:|
| Samples | 120 |
| Final task count | 337 |
| Succeeded | 25 |
| Failed | 312 |
| Queue delivery failures | 0 |
| API unhandled failures | 0 |
| State database size | 962,560 bytes |
| API monitoring latency (average) | 6,233.96 ms |
| API monitoring latency (p95) | 6,314.07 ms |

The monitor's process-memory field reported zero because both child applications were hosted by `dotnet` and the script matched the wrong process name. Manual observation during the run was approximately 183 MB for the API and 126 MB for the Collector. The script must be corrected before memory figures are used as an acceptance measurement.

## Findings and remediation

1. `RaceCard` and `RaceResult` tasks discovered without a `domainRaceId` used the logical resource key as if it were an existing domain race ID. The refresh endpoint consequently returned 404. The handlers and workflows now use create/upsert semantics when `domainRaceId` is absent and retain refresh semantics when it is present.
2. `CollectionPlanningScheduler` recreated the same three-hour discovery bucket after its previous task reached a terminal state. It now treats the persisted `CollectionState` as durable evidence that the bucket has already been planned.
3. Monitoring endpoint latency remained near 6.2 seconds. This is tracked as an unresolved performance finding; endpoint-level timing must be measured after the correctness fixes under a fresh run.
4. Some current-race navigation returned a page kind other than `RaceCard`. The type and race-identity validation worked, but availability/error classification needs to be assessed in the verification run.

## Verification required

- Run a fresh local queue verification and confirm first-time race writes no longer return 404.
- Confirm the task count does not grow once the single discovery bucket is terminal.
- Record per-endpoint latency and correct PID-based memory sampling.
- If the short verification passes, repeat the two-hour soak for final operational acceptance.

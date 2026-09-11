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

## Ten-minute corrective verification (2026-09-12)

- Duration/samples: 10.02 minutes / 10
- Stable task count after discovery expansion: 151
- At the final sample: succeeded 47, failed 7, ready 96, running 1
- Queue delivery/worker failures: 0
- Previous `RaceResult` 404 / `DomainWriteRejected`: 0
- API latency: 46.96 ms average, 139.24 ms p95 (first cold sample); steady-state 27-45 ms
- Memory range observed: API 158-229 MB, Collector 35-142 MB

The seven failures were all discovery attempts for the current/future dates included by the test's September 2026 batch, classified by navigation as unavailable/not published. They were not queue or domain-write failures. The soak launcher now selects the previous calendar month so subsequent backfill verification contains past dates only.

The earlier 6.2-second measurement was caused by the monitor and Collector using `localhost` while the API listened only on IPv4. Each PowerShell request incurred an IPv6 fallback delay. The local verification tools now consistently use `127.0.0.1`; no collection-platform performance change was needed for that finding.

## Verification required

- Run a fresh local queue verification and confirm first-time race writes no longer return 404.
- Confirm the task count does not grow once the single discovery bucket is terminal.
- Record per-endpoint latency and correct PID-based memory sampling.
- If the short verification passes, repeat the two-hour soak for final operational acceptance.

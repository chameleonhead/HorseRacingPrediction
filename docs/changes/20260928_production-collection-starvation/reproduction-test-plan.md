# Mandatory dispatch-starvation reproduction tests

Status: Approved. This artifact specifies tests only; it changes no production code and performs no production access. Local test execution is within the approved implementation scope. It does not authorize production access or deployment.

## Gate and purpose

The regression proof is mandatory for AC3 and AC4, not an optional confidence check. Before changing production behavior, the test owner must run the deterministic reproduction against the pre-fix behavior and capture the expected red result. The test must fail for the precise starvation invariant below, not because setup, compilation, routing, or an unrelated assertion failed. Preserve the fixture builder and its parameters in source control (do not check in a SQLite database binary), then run the same scenario after the fix and require it to pass. If the pre-fix test does not reproduce the failure, stop and revise the hypothesis/design before implementing the fix.

Run against the deployed implementation semantics from `ab1cda45` (or a source checkout that is proven equivalent at the relevant query/dispatch/acquire/Lambda boundaries). Record the exact source revision/build used for the red run. The supplied production counters motivate this test but do not prove that this state is present in production. T1's separately authorized, count-only aggregate remains the production-prevalence gate; these tests are local only.

## Production-shaped local fixture

Use a temporary file-backed SQLite database, a fixed/test clock, the real SQLite-backed `CollectionPlatformStore`, real `CollectionPlatformOutboxDispatcher`, a local deterministic queue adapter implementing send/receive/visibility/redelivery/partial-batch semantics, the real API endpoint hosted over local HTTP, the real `CollectionPlatformWorkerClient`, and the real `CollectionLambdaInvocation` wake path. The queue adapter is the only boundary substitute; do not mock the store, dispatcher, HTTP API/client, or Lambda invocation. Use the exact v1 acquire route and JSON semantics for the baseline where applicable. The post-fix E2E must use the approved HTTP 201 Acquired / HTTP 200 typed NoWork JSON contract and verify client deserialization end to end.

The reusable source fixture builder (proposed name `CollectionDispatchStarvationFixture.CreateAsync`) must seed `MaxInFlightEnvelopes=1`, an eligible `Ready` Background task with one due, undispatched, unheld outbox row at the task's current generation, and a higher-priority candidate that the deployed selector can reserve but the acquire endpoint rejects. Run two separate stale-candidate variants so each cause is proven independently:

1. A higher-priority task is still `Ready`, but its due outbox row has a stale dispatch generation.
2. A higher-priority due outbox row belongs to a terminal or otherwise non-`Ready` task (with a generation that would otherwise match).

For each variant, exercise a real dispatcher cycle, take the emitted wake through the local queue, invoke the real Lambda HTTP acquire boundary, and return the typed `NoWork` JSON result from the real test API/store. Advance the fixed clock only within the configured unexpired reservation window and execute a bounded number of real poll/dispatch/receive cycles.

## Required red/green assertions

### Pre-fix red run

Against source proven equivalent to `ab1cda45`, assert and report all of these observations:

- The higher-priority stale/ineligible row is selected and reserved ahead of the valid Background row.
- The real acquire endpoint returns typed `NoWork`; the deployed wake handler acknowledges that decoded result.
- The invalid row's reservation remains active and consumes the only `MaxInFlightEnvelopes` slot; the persisted lane cursor/dispatch state does not advance for that wake-only result.
- Within the documented bounded cycles and before reservation expiry, the valid Background task is not acquired.
- The test fails specifically on the starvation expectation that Background should have been acquired within the bound. Any earlier setup or unrelated failure is an invalid reproduction, not a passing red gate.

The source comparison supporting the expected red behavior is: deployed `GetPendingDispatchesAsync` does not filter task `Ready` status or current generation; deployed `AcquireNextExecutionAsync` rejects terminal/non-Ready or stale-generation work as `NoWork`; `MarkWakeSentAsync` does not mark the wake-only row dispatched; and the current deployed Lambda acknowledges typed `NoWork`. With the single slot occupied, the later valid Background candidate is not reached before the stale reservation expires.

### Post-fix green run

Run the identical seed and bounded cycle assertions after implementation:

- The stale row is filtered before reservation, or the transactional reservation recheck rejects it and continues the bounded scan to the valid Background candidate.
- The Background task is acquired within the explicit poll/cycle bound, with lane/fairness state persisted as designed.
- If an already-created reservation is eligible for cleanup, release is scoped to the exact wake/envelope/reservation token and current generation, and only when there is no active execution lease. A mismatched token, changed generation/status, ambiguous row cardinality, or live lease leaves the row untouched; it cannot release another worker's reservation/lease.
- A typed `NoWork` release-precondition failure is an idempotent bounded outcome; it does not turn into an unbounded retry loop or clear a replacement reservation.

## Mandatory companion cases

The integrated test suite must also prove:

- Transaction race: the task becomes terminal/non-Ready, and separately rotates generation, after candidate selection but before reservation commit. The immediate transaction recheck rejects each invalid candidate and continues to the valid Background candidate without reserving stale capacity.
- Duplicate wake: redelivery of the same wake is idempotent and never creates a second active execution lease.
- Lambda retry boundary: HTTP 5xx, request timeout, connection loss, and ambiguous response loss after an API commit return the original SQS message ID in `batchItemFailures`; they are not acknowledged as `NoWork`. Retrying the same wake remains safe. A successfully decoded typed `NoWork` is acknowledged.
- Restart persistence: restart the local API/dispatcher process after reservation/send/acquire boundaries using the same SQLite file and prove persisted cursor, reservation, wake identity, and lease state preserve fairness/idempotency.
- Delay policy: preserve deployed `race-odds` aggregation-delay bypass, while an equally aged non-`race-odds` task remains delayed; cover selection and reservation-time revalidation.
- Exact-token release: a release token mismatch and an active execution lease are non-mutating and cannot affect the valid Background task or another worker's lease.

## Ownership, commands, and completion evidence

T9's core fixture and pre-fix red run are recorded at commits `773085dd` and `31cd0444`. The real-path red evidence confirms the mechanism and is sufficient to begin local implementation; T1 production prevalence is not a local implementation gate. T9 remains `In progress` for the deterministic selection/reservation race seam and companion cases. After this checkpoint is committed, Lead dispatches T3 as the exclusive sequential coding owner; T3 closes those remaining T9 test cases before or alongside the corresponding fix and runs unchanged seeds green. Do not edit fixture/test files concurrently with T3. T4 owns endpoint/client assertions for the approved HTTP contract; T4b owns instrumentation/alert tests. T3/T4/T4b remain `Dependent` until each is explicitly dispatched after the preceding handoff. The anticipated focused commands are:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --filter FullyQualifiedName~CollectionDispatchStarvationReproductionTests
dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --filter FullyQualifiedName~CollectionLambdaInvocationTests
```

If implementation uses different fixture/test names or project boundaries, update the command and write scope in the approved record before execution. The evidence must include: pre-fix source revision and the single intended failing assertion for both stale-candidate variants; unchanged seed/fixture identity; post-fix focused test results from T3; production-shaped SQLite + dispatcher + local queue + HTTP API + real Lambda E2E results; and the relevant full-project/build regression results. A mocks-only test suite does not satisfy this gate.

## T9 execution evidence (2026-09-28)

- The deployed revision is `ab1cda45`; the local baseline is `f6ca4985e0bd7a8a29bbea1dabe7ffd7e68ff0c6`. Diff inspection found the dispatch query/reservation code in `CollectionPlatformStore`, `CollectionPlatformOutboxDispatcher`, and `CollectionLambdaInvocation` unchanged between those revisions. The store has unrelated later changes elsewhere. Worker HTTP paths changed during v2 routing, so this harness exercises the real current v2 HTTP endpoint/client while proving the same store acquisition and Lambda NoWork behavior; it does not claim the route itself is byte-identical to deployed v1. The deployed v1 route is `/api/internal/collection/executions/acquire-next`; current main routes it through `/api/v2/internal/collection/execution-leases`.
- Added `tests/HorseRacingPrediction.Api.Tests/CollectionDispatchStarvationReproductionTests.cs`. It uses file-backed SQLite, the real dispatcher, a deterministic local wake queue, the API test host/client, and `CollectionLambdaInvocation`. Two independent fixtures put either an outbox generation ahead of a still-Ready task or a matching-generation outbox on a DeadLetter task ahead of a valid Background task. Each confirms the invalid row receives the sole unexpired reservation, HTTP returns typed `NoWork`, Lambda acknowledges it, persisted lane state remains empty, and two further dispatch polls do not reach the Background row. A separate test verifies `race-odds` bypasses a configured aggregation delay while a similarly aged `race-result` stays unreserved; focused result: 1 passed.
- The normal characterization run passed both rows: 2 passed. To obtain red evidence without leaving the normal suite failing, set `COLLECTION_STARVATION_EXPECT_FIXED=1`; the same fixtures then fail only at the final bounded Background-dispatch assertion. Both rows failed there with `DispatchedAt == null`; all preceding setup, selection, typed-NoWork, Lambda acknowledgement, live-reservation, and lane-cursor assertions passed. The seed is identical between normal and expected-fixed runs.
- Focused Lambda invocation suite: 14 passed. Broad API suite: 329 passed, 1 skipped. Broad Collector suite: 393 passed. Existing `Wake_AcquireBadGatewayIsAcknowledgedForScannerRecovery` confirms current 5xx acknowledgement behavior, which conflicts with the approved retry boundary and remains for T3 to correct. The real-path characterization observes HTTP 201 JSON for typed NoWork on current main; aligning that to the approved 200 typed-NoWork contract remains T4 work.
- T9 remains `In progress` for a deterministic state/generation change between selection and reservation (transaction seam or equivalent barrier), active holds, Ready-without-outbox/cardinality anomalies, duplicate/expired reservation behavior, ambiguous-response retry, and restart persistence. `race-odds` delay bypass is already covered 1/1. These remaining cases move into T3's sequential closure work; no production seam or production code was changed in the T9 slice.

No reproduction test contacts production, AWS, SSH, or the live queue. Passing local tests do not replace T1's independently reviewed and authorized aggregate for production-prevalence claims or production acceptance. The real-path T9 reproduction permits local implementation to proceed without T1. Approval authorizes the scoped local implementation, but does not authorize deployment or broader production operations.

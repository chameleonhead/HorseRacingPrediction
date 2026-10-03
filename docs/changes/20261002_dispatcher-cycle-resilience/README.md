# Collection dispatcher resilience and lane activity visibility

- Status: Approved
- Change record schema: 2
- Owner: Collection operations
- Created: 2026-10-02
- Updated: 2026-10-03

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Implemented | Dispatcher resilience, additive lane activity contract, store aggregation, and `/jobs` grid are complete. |
| Verification | Local passed | API 418 passed/1 existing skip; Contracts 61 passed; ApiClient 24 passed; focused refresh test passed; build and formatter passed. |
| Deployment/operation | Recovery in progress | Same-revision deployment run 37037800539 succeeded, but the three lanes remained at zero Running. AWS evidence now identifies expired dispatch reservations; T5 is implementing the corrective closure item before redeployment. |

## Context

At 2026-10-02 23:08 JST production had 3,578 `Ready` tasks and zero `Running` tasks while the pipeline was
unpaused and the failure dashboard was empty. A complete read-only task inventory showed due work in every lane:

| Lane | Ready | Due now | Oldest due |
| --- | ---: | ---: | --- |
| Realtime | 610 | 584 | 2026-09-30 22:44 JST |
| Normal | 2,427 | 2,427 | 2026-09-30 22:42 JST |
| Background | 541 | 541 | 2026-10-02 04:26 JST |

The persisted tomorrow and following-day race cards are available through the public API, but subject refresh and
historical work no longer progress. A pause/resume at 23:11 JST safely released unleased reservations but produced
no task transition during a two-minute observation window. The dispatcher hosted loop calls `DispatchOnceAsync`
without a cycle-level exception boundary. Per-envelope queue failures are caught, but store, reclaim, fairness,
candidate-query, or telemetry exceptions before that boundary can end the hosted loop.

## Goals

- Keep dispatch alive after a transient non-cancellation failure in one cycle.
- Preserve the approved lane policy: up to four Realtime envelopes, then service Normal and Background in turn.
- Ensure future-dated Realtime work never makes due Normal or Background work ineligible.
- Restore production processing and verify terminal progress in all three lanes without deleting tasks or queues.
- Show operators the due, running, last-started, and last-completed facts for every lane on `/jobs`.

## Non-goals

- Changing task priorities, the 4:1 non-Realtime service boundary, or compatibility grouping.
- Purging SQS, cancelling backlog, rewriting task timestamps, or treating future-dated work as due.
- Suppressing deterministic collection failures or weakening pipeline pause, lease, and capacity guards.
- Guessing a health label from an arbitrary inactivity threshold or replacing monitoring alerts with the UI.

## Documentation updates

- `docs/22-collector-design.md`: records the canonical due-candidate, lane-fairness, and cycle-resilience contract.
  This change record contains the incident-specific evidence and delivery gates.
- `docs/20-admin-ui-design.md`: records the lane activity table, timestamp semantics, and narrow-width behavior.
- `mocks/jobs-lane-activity.md`: records the approved information hierarchy without prescribing incidental CSS.

## Technical impact

- `src/HorseRacingPrediction.Api/CollectionController/CollectionPlatformOutboxDispatcher.cs`: isolate each hosted
  dispatch cycle, rethrow only host cancellation, log the full transient exception, delay, and continue.
- `tests/HorseRacingPrediction.Api.Tests/CollectionPlatformOutboxDispatcherTests.cs`: add a hosted-loop regression
  where the first pre-envelope cycle fails and a later cycle dispatches work.
- `CollectionProgressSnapshot` and its additive API DTO expose one activity row for every `CollectionLane`. Each row
  contains due Ready count, Running count, latest persisted `StartedAt`, and latest persisted `FinishedAt`.
- `CollectionPlatformStore.GetProgressAsync` computes lane activity from the same task snapshot already loaded for
  progress, using one captured current time for due filtering; it adds no second task-table scan.
- `/jobs` renders the rows in Realtime, Normal, Background order in a compact Fluent data grid. Timestamps are JST,
  missing history is `実績なし`, and the existing browser refresh time remains separately labelled `最終更新`.
- The additive response affects `GET /api/v2/admin/collection/operations/progress` and the nested progress value in
  `GET /api/v2/admin/collection/operations/dashboard`; existing properties and meanings remain unchanged.
- Existing allocator and starvation tests remain the independent evidence for lane proportions. Add or retain an
  explicit counterexample where Realtime is future-dated and due Normal/Background work proceeds.

## Hypothesis ledger

| ID | Claim | Fact/inference boundary | Supporting and contradicting evidence | Falsification | Result and disposition |
| --- | --- | --- | --- | --- | --- |
| H1 | Future-dated Realtime tasks block other lanes. | Production concern; falsified by code and task inventory. | `GetPendingDispatchesAsync` and `CollectionLaneAllocator.Select` require `AvailableAt <= now`; 2,968 Normal/Background tasks are already due. | Inventory all Ready tasks and compare `AvailableAt` with JST now. | Falsified; no lane-policy change is needed. |
| H2 | The dispatcher hosted loop is no longer cycling. | Strong inference; exact triggering exception is not persisted in the admin API. | Zero Running and no state change across two minutes despite 3,552 due tasks; pause/resume releases reservations but does not restart the service; `ExecuteAsync` has no non-cancellation exception boundary. | Restart the same revision and observe new dispatch/terminal progress without data mutation. | In progress through run 37018420749; the durable design does not depend on the exact transient exception type. |
| H3 | A stale reservation alone explains the stall. | Falsified for the current incident. | The approved pause/release/resume path completed, but counts did not change and Running stayed zero for two minutes. | Pause/resume and observe task transitions. | Falsified; retain reservation safety unchanged. |
| H4 | Catching a cycle exception can preserve safety invariants. | Design claim requiring isolated reproduction. | Per-envelope failures already use catch-and-continue; cancellation and pipeline pause must remain authoritative. | Inject one pre-envelope transient failure, then prove a later cycle dispatches; separately cancel and prove prompt exit. | Proposed verification in T1. |
| H5 | The current admin screen already exposes enough lane-liveness evidence. | Falsified by contract and component inspection. | `/jobs` shows a browser refresh time and the progress contract only exposes `ActiveTasksByLane`; neither last execution timestamps nor due/running split is available. | Inspect `Jobs.razor`, `CollectionProgressSnapshot`, DTO mapping, and store aggregation. | Falsified; AC5-AC6 add authoritative lane activity. |
| H6 | The wake reaches Lambda only after its reservation has already expired because the dispatcher reuses the cycle-start time when writing the reservation deadline. | Confirmed production fact plus direct code trace. | From 2026-10-02 20:22 JST onward CloudWatch recorded zero successful acquisitions and 195 `ReservationUnavailable` acquisitions over 24 hours. The dispatcher continued to emit `Reserved` and `WakeSent`; Lambda started in the same minute and returned without a task. `DispatchOnceAsync` captures `now` before a production cycle that currently takes about four minutes, then `ReserveDispatchesWithinCapacityAsync` persists `ReservedUntilUnixMilliseconds = now + 45 seconds`. | Use the existing pre-reservation test hook to advance a controllable clock beyond 45 seconds, then acquire the emitted wake through the real store boundary. | Confirmed. Preserve the selection snapshot for eligibility/fairness but calculate the reservation deadline from a fresh timestamp immediately before reservation. |

## Decisions

1. Add the resilience boundary around one `DispatchOnceAsync` invocation in the hosted loop, not around individual
   store mutations. This keeps existing transactional and reservation semantics unchanged.
2. `OperationCanceledException` caused by the host token exits normally. Other exceptions are logged with the
   exception object and the service waits the configured dispatch interval before retrying, preventing a hot loop.
3. Lane selection remains based only on due candidates. The existing four-Realtime burst and alternating
   Normal/Background policy is frozen and must pass the real dispatcher/store regression.
4. Deployment and production verification are part of completion: restarting the service alone is temporary
   recovery, not closure.
5. Lane activity timestamps mean persisted task lifecycle facts: `LastStartedAt` is the maximum non-null `StartedAt`
   and `LastCompletedAt` is the maximum non-null `FinishedAt` in that lane. They are not page-refresh, queue-send, or
   inferred heartbeat timestamps. Due count is `Ready && AvailableAt <= capturedNow`; Running count is status
   `Running`. All enum lanes are returned even when every value is zero/null.
6. The UI presents facts rather than a derived healthy/stalled badge. Alert thresholds belong to monitoring; this
   avoids declaring a low-volume Background lane unhealthy merely because it legitimately had no recent work.
7. Candidate eligibility, fairness, lease, and capacity revalidation continue to use the cycle-start snapshot.
   Reservation lifetime alone starts from a fresh timestamp obtained immediately before the reservation transaction,
   so slow candidate discovery cannot create an already-expired wake. The store accepts this second timestamp only for
   calculating `ReservedUntilUnixMilliseconds`; the 45-second duration and every pause, lease, capacity, priority,
   and compatibility guard remain unchanged.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | A broad catch could hide a persistent programming error. | Infinite retry could conceal a defect while backlog grows. | Log the full exception every failed cycle, retain monitoring/watchdog findings, use the normal interval as backoff, and do not catch cancellation. A repeated failure remains observable and rollback is the prior image. | AC1, AC4 / T1,T3 / persistent-failure log test and production monitoring | Agree with bounded catch-and-observe; object to silent swallowing. | Approved with this record on 2026-10-02. | Resolved in design |
| C2 | Changing fairness while fixing liveness could starve Realtime or Background. | Violates the existing operational allocation. | Freeze allocator behavior; add future-Realtime and mixed-lane counterexamples and make no priority/config changes. | AC2, AC3 / T1 / dispatcher fairness tests | Agree. | Approved with this record on 2026-10-02. | Resolved in design |
| C3 | Restarting production can duplicate work. | Duplicate delivery could corrupt state. | Use the canonical deployment pause/drain route; existing generation, reservation-token, and idempotent completion guards remain unchanged. Do not purge SQS or rewrite tasks. | AC4 / T2,T3 / workflow and production evidence | Agree; same-revision restart is the narrowest reversible stabilization. | Approved with this record on 2026-10-02. | Resolved in design |
| C4 | Exact exception details are absent from the admin API. | Root trigger may recur in a different subsystem. | Treat exact exception type as an evidence gap, not an approved premise. The correction covers transient failures at every pre-envelope cycle boundary and records future exceptions. | AC1, AC4 / T1,T3 | Agree; this does not justify broader data or queue mutation. | Approved with this record on 2026-10-02. | Accepted risk |
| C5 | A timestamp labelled as execution activity could accidentally show browser refresh or queue-send time. | Operators could believe stalled work is progressing. | Define and test persisted task `StartedAt`/`FinishedAt` semantics, label screen refresh separately, and render missing lifecycle history as `実績なし`. | AC5 / T2,T3 / store, mapper, and component tests | Agree; lifecycle facts are the least ambiguous evidence. | Approved with this record on 2026-10-02. | Resolved in design |
| C6 | A derived red/green status needs an arbitrary inactivity threshold and can misclassify low-volume Background work. | False alarms or false reassurance. | Show due/running counts and exact lifecycle timestamps; retain the existing monitoring subsystem for alert classification. | AC5,AC6 / T2,T3 | Agree; raw operational facts are actionable without a new policy. | Approved with this record on 2026-10-02. | Resolved in design |
| C7 | Adding progress fields could break existing clients or add an expensive query. | Admin pages or operational polling could regress. | Use one additive DTO property, retain all existing fields, aggregate the task snapshot already loaded by `GetProgressAsync`, and regression-test both progress and dashboard response shapes. | AC5,AC6 / T2 / contract tests | Agree. | Approved with this record on 2026-10-02. | Resolved in design |
| C8 | Refreshing the single store timestamp at reservation would also change future-work eligibility, lease/capacity revalidation, and lane fairness after a slow scan. | The incident fix could violate AC2-AC3 while restoring throughput. | Pass a separate reservation-deadline timestamp to the store and use it only for `ReservedUntilUnixMilliseconds`; retain the cycle snapshot for every existing query and fairness update. Add a delayed pre-reservation counterexample where Realtime remains future at the cycle snapshot and rerun existing fairness regressions. | AC2-AC4 / T5 / delayed-reservation and existing starvation tests | Object to replacing the existing store `now`; agree with the split-clock correction. | Approved as a local closure refinement; no public contract or frozen lane decision changes. | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | After one non-cancellation exception before envelope dispatch, the hosted dispatcher logs it, waits without a hot loop, and a later cycle dispatches eligible work without process restart. | T1 | hosted-service regression with a fail-once dependency | Verified |
| AC2 | When Realtime tasks exist but are future-dated, due Normal and Background tasks are dispatched; future Realtime tasks remain untouched. | T1 | real store/dispatcher counterexample | Verified |
| AC3 | Under due three-lane load, dispatch order retains four Realtime grants followed by Normal, then four Realtime grants followed by Background; priorities and compatibility grouping remain unchanged. | T1 | existing and focused fairness/starvation regressions | Verified |
| AC4 | CI and canonical deployment pass; production remains unpaused with zero new failures and shows terminal progress from Realtime, Normal, and Background during a bounded observation window. | T2,T3,T5 | delayed-reservation regression, CI/deploy runs, CloudWatch acquisition evidence, plus before/after task evidence by lane | Connected |
| AC5 | `/jobs` always displays Realtime, Normal, and Background in that order with due Ready count, Running count, latest task start, and latest task completion. Values come from persisted server state; timestamps use JST and absent timestamps display `実績なし`. | T2 | store/mapper/API regressions and bUnit component assertions including zero/null Background history | Verified |
| AC6 | Automatic/manual refresh updates the lane activity table without navigation, the existing page-refresh timestamp remains distinct, and the table remains readable at narrow width without hiding Background. Existing progress/dashboard clients retain their prior fields. | T2 | bUnit refresh and responsive markup assertions; API compatibility tests; focused browser verification | Connected |

## Task plan

| ID | Task | Owner | Model tier | Routing | Depends on | Write scope | Verification | Completion evidence | Audit | Result metrics | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Add cycle resilience and production-shaped liveness/fairness regressions. | Lead planner/executor/verifier | gpt-6-luna high requested; observed unavailable | Cheap execution; overlapping writes in dispatcher and tests make delegation review cost exceed benefit. | Approval | dispatcher and its focused tests only | focused dispatcher/starvation tests, full API suite, build, formatter, CodeGraph sync | AC1-AC3 executable evidence | none | unavailable; retries 0; corrections 0; reviews 1 | Verified |
| T2 | Add authoritative per-lane progress aggregation, additive contracts, `/jobs` table, and focused tests. | Lead planner/executor/verifier | gpt-6-luna high requested; observed unavailable | Cheap execution; public contract integration is serialized to prevent a mismatched intermediate state. | Approval | progress model/store/DTO/mapper/client, `Jobs.razor` and scoped styles, focused API/component tests | focused store/API/component tests, full affected suites, build, formatter, CodeGraph sync | AC5-AC6 executable evidence | none | unavailable; retries 1; corrections 1; reviews 1 | Verified |
| T3 | Deploy through the canonical workflow. | Lead operator | mechanical | Integration operation depends on the verified revision and remains Lead-owned. | T1,T2 | workflow operation only | CI and deployment terminal success | run URLs and deployed commit | none | unavailable; retries 0; corrections 0; reviews 0 | Runnable |
| T4 | Verify production progress, screen evidence, and close the incident record. | Lead verifier | lead acceptance | Final acceptance remains Lead-owned. | T3 | authenticated diagnostics, browser verification, and change record only | lane-specific before/after facts, pipeline/failure state, rendered `/jobs` evidence | AC4-AC6 evidence and final review | none | unavailable; retries 0; corrections 0; reviews 0 | Dependent |
| T5 | Correct the stale reservation clock, add the production-shaped delay regression, deploy, and verify three-lane recovery. | Cheap Executor; Lead integrates and accepts | gpt-6-luna high requested; observed model unavailable because runtime telemetry does not expose it | Cheap execution; split-clock refinement after the first focused test exposed a fairness crossover boundary; deployment and final acceptance remain Lead-owned. | H6 confirmed; existing approval and AC2-AC4 | `src/HorseRacingPrediction.Api/CollectionController/CollectionPlatformOutboxDispatcher.cs`; `src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/CollectionPlatformStore.cs`; `tests/HorseRacingPrediction.Api.Tests/CollectionDispatchStarvationReproductionTests.cs` | focused starvation tests; full API suite; API build; exact CI formatter; CodeGraph sync; audit/DDD validators; `git diff --check`; canonical deploy; CloudWatch and `/jobs` observation | delayed reservation acquires successfully, unchanged fairness regressions pass, deployed production shows successful acquisitions and lane progress | T5-A1 | unavailable; retries 0; corrections 0; reviews 1 | In progress |

## Review gates

- **Design and task-split review:** Complete for approval. The liveness correction is isolated to the hosted loop;
  allocator policy is frozen. The activity display is an additive progress projection over persisted lifecycle facts,
  not a second monitoring policy. AC1-AC3 cover dispatcher behavior, AC4 the real deployment path, and AC5-AC6 the
  API/UI evidence. Shared contracts make serialized implementation lower risk than parallel editing.
- **Concern and agreement review:** C1-C3 and C5-C7 are resolved in design. C4 is an accepted observability risk: the exact
  original exception is unavailable, but the proposed boundary is independent of its transient subtype and future
  occurrences become logged. User disposition is pending approval of this record.
- **Pre-implementation review:** Passed on 2026-10-02 after explicit user approval. Public-contract changes are additive;
  dispatcher cancellation, fairness, priorities, and compatibility invariants remain frozen.
- **Checkpoint review:** Passed locally. The integrated diff preserves allocator and cancellation invariants; added tests
  cover cycle continuation, future-dated exclusion, all-lane projection, null history, JST display, and refresh.
- **T5 pre-implementation review (2026-10-03):** `agent-task-orchestration` and
  `document-driven-development` were loaded in the current turn. Production evidence confirms H6. The Cheap Planner
  inspected the dispatcher, store boundary, test hooks, and existing regressions. Purpose: start the reservation lease
  at reservation time without changing the candidate snapshot. Files: dispatcher, focused starvation test, and this
  record. Steps: inject/use the existing testable clock pattern; obtain a fresh timestamp after the pre-reservation
  hook; add a >45-second delayed-reservation acquisition regression; run focused and full verification; deploy and
  observe. Unresolved specification questions: none. Counterexample: future-dated Realtime remains untouched while
  due Normal/Background remain eligible. Commands: focused starvation test, full API tests, API build,
  `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`, `codegraph sync .`, audit and DDD
  validators, and `git diff --check`, all expected to pass. Route: requested `gpt-6-luna` with `high` reasoning;
  the planner accepted and completed, and the same available worker is assigned as executor. Observed model and usage
  remain null because the runtime exposes no independent telemetry. T5 is `In progress`; production writes remain
  Lead-owned.
- **T5 Router reassessment (2026-10-03):** The first focused implementation passed 37 starvation and 10 dispatcher
  tests but exposed a scope boundary: replacing the store's single `now` would also move the eligibility/fairness
  snapshot when a future Realtime task crosses its due time during the delay. This is a local repository-design gap,
  not a requirement ambiguity. The cheap route continues with one added store file and a split-clock contract: all
  existing revalidation uses the original `now`; only reservation expiry uses the fresh timestamp. No public contract,
  AC, risk disposition, or lane decision changes, so reapproval is not required.
- **Final review:** Pending deployment and production evidence.

## Incident record

```text
Incident: 2026-10-02 23:08 JST; 3,578 Ready, 3,552 due, zero Running, all three lanes stalled.
Temporary recovery: Pause/resume and same-revision deployment 37037800539 did not recover task execution.
Root cause: The dispatcher reuses its cycle-start timestamp after a roughly four-minute candidate scan, so the
45-second reservation is already expired when the wake is sent. Lambda acknowledges `ReservationUnavailable` and
does not acquire a task. The missing cycle exception boundary was a separate resilience defect already corrected.
Corrective proposal: Keep candidate/fairness evaluation on the cycle snapshot, but start the unchanged reservation
duration from a fresh pre-reservation timestamp; reproduce the delay through the store and Lambda acquisition path.
Permanent fix: Approved; T5 implementation in progress.
Remaining risk: The reservation transaction itself is expected to remain well below 45 seconds. If later telemetry
shows otherwise, moving deadline calculation inside the transaction requires a separate design review.
```

## Verification record

- Read-only production inventory at 2026-10-02 23:08 JST reconciled all 3,578 Ready tasks and 3,552 due tasks.
- A 2026-10-02 23:11 JST pause/release/resume left Running at zero and state counts unchanged through 23:13 JST.
- CodeGraph traced due filtering, lane allocation, capacity reservation, hosted-loop registration, and the missing
  outer exception boundary. Existing dispatcher tests cover fairness but not hosted-loop continuation after failure.
- AWS read-only diagnostics on 2026-10-03 confirmed an enabled SQS event source mapping, Lambda reserved concurrency
  1, empty main queue and DLQ between wakes, recurring Lambda invocations with zero platform errors, and repeated
  typed `ReservationUnavailable` results. Successful acquisitions ended at 20:22 JST while reservations and wakes
  continued through the current observation window.
- T5 focused verification passed 47/47 and the full API suite passed 419 with one existing skip. The first exact
  formatter verification failed before formatting because the isolated worktree had not restored solution-wide test
  dependencies; unrelated test projects reported missing MSTest symbols. Cause category: environment/dependency
  preparation. `dotnet restore HorseRacingPrediction.sln` completed and the identical formatter verification then
  passed, closing the failure. The API build passed with zero warnings and errors. `codegraph sync .` could not run
  because the managed worktree does not contain the repository's ignored `.codegraph` index; no post-change graph
  conclusion is claimed. Source-level caller compatibility is instead covered by the successful build and API suite.
- T5 checkpoint review accepted patch `3020cacc71372b0d28b74c3532ac93615b19dc15`: only reservation expiry uses the
  fresh clock, while the original `now` remains on all lease, capacity, eligibility, aggregation, fairness, and cursor
  paths. The production-shaped test independently crosses the future-Realtime due boundary during a 46-second
  simulated delay and still acquires the selected non-Realtime wake. Deployment and production observation remain.

## Deviations and follow-up

No approved implementation exists yet. If deployment restart does not restore dispatch, H2 returns to investigation
and this record remains `Proposed`; code implementation will not proceed on an invalidated premise.

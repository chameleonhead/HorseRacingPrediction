# Collection processing count consistency

- Status: Approved
- Change record schema: 2
- Owner: HorseRacingPrediction team
- Created: 2026-09-20
- Updated: 2026-09-20
- JRA site contract impact: None

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Production diagnosis | Complete | The displayed count included tasks whose Running lease had already expired. |
| Code | Complete | The processing count now includes only currently valid Running leases. |
| Verification | In progress | Store and component regressions passed; a production read-back remains. |
| Error correction | Excluded | Dispatch compatibility, Lambda errors, SQS recovery, retries, and task-state mutation are explicitly outside this change. |

## Scope and decision

The Jobs page `処理中` badge currently counts the latest task row whenever `Status == Running`, even if `LeaseExpiresAt` is already in the past. Lease recovery is performed elsewhere, so a stalled dispatcher or failed Lambda can leave the badge temporarily much higher than the number of tasks that can still be executing.

The user directed this change to correct only the processing count and not the underlying collection errors. The count will include only latest tasks whose status is Running and whose lease expiry is later than the current time. The read does not reclaim, retry, cancel, complete, or otherwise mutate a task.

## Concern and agreement ledger

| ID | Concern | Evidence and impact | Recommended disposition | Alternatives | Residual risk | AC/task/counterexample | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Counting status alone reports expired work as processing. | Production count fell as lease recovery ran; source count ignores `LeaseExpiresAt`. | Count only unexpired Running leases. | Reclaim leases in the GET request. | A clock boundary can change the badge between refreshes, which is intended. | AC1/T1; expired Running row. | Use a read-only predicate. | User limited work to count consistency. | Resolved in design |
| C2 | Fixing task state or Lambda errors would exceed the requested scope. | Compatibility errors were diagnosed separately. | Do not change worker, dispatcher, SQS, lease recovery, or error handling. | Bundle the error fix. | Underlying failures may continue, but the count will no longer represent expired leases as active processing. | AC2/T1. | Keep the change isolated. | Explicitly excluded by user. | Excluded follow-up |

## Acceptance criteria

| ID | Criterion | Task | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | `処理中` counts a latest Running task only while its lease is unexpired; an expired Running task contributes zero without changing its stored status or attempt history. | T1 | Store counterexample and existing latest-task-count tests. | Verified |
| AC2 | No production path for dispatch compatibility, Lambda execution, SQS, retry, task recovery, or failure classification is changed. | T1 | Diff inventory and focused regressions. | Verified |
| AC3 | After deployment, the badge/API count equals the number of latest Running tasks with currently valid leases. | T2 | Production task-view count and task lease evidence. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Filter the task-view Running count by lease validity and add regression coverage. | Main | Lead | User scope direction | store count method and focused store tests | focused store tests, build, format, diff | Active lease counts one; expired lease counts zero; no task mutation. | Verified |
| T2 | Deploy once and verify the production count. | Main | Lead | T1 | deployment and read-only production diagnostics | API count and task lease evidence | Production count matches valid leases. | In progress |

## Review gates

- **Design and task-split review:** This is a single predicate plus regression test; delegation overhead exceeds the implementation cost. Production deployment remains a separate dependent step.
- **Concern and agreement review:** C1 is resolved by a read-only predicate. C2 records the user's explicit exclusion of error correction.
- **Pre-implementation review:** T1 is Runnable with exclusive ownership of the count method and test. T2 is Dependent. Test must prove no mutation.
- **Checkpoint review:** T1 passed 93 store tests, 12 API component tests, solution build, formatter verification, and diff scope review. The regression also reads task and attempt rows directly to prove the count request does not mutate them.
- **Final review:** Requires AC1-AC3 Verified and no error-path changes.

## Incident evidence retained

- At 11:44 JST the management API had 2 actual Running task rows while the earlier UI report was 51.
- The count subsequently moved as leases were reclaimed, confirming that stale Running rows contributed to the display.
- SQS/Lambda compatibility errors remain diagnosis only and are not corrected by this change.

## Documentation updates

- This record is the canonical scope for the processing-count correction.
- No architecture or operational document changes are required because task lifecycle behavior is unchanged.

## Rollback

Revert the count predicate and its test. This change performs no data migration or production task mutation.

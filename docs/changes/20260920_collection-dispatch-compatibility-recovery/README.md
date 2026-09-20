# Collection dispatch compatibility recovery

- Status: Proposed
- Change record schema: 2
- Owner: HorseRacingPrediction team
- Created: 2026-09-20
- Updated: 2026-09-20
- JRA site contract impact: None

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Production stabilization | Partial | Running tasks naturally fell from 51 reported, to 2, to 1, to 0 as leases expired. No mutation was performed. SQS still had 64 invisible wake messages at 11:47 JST. |
| Root cause | Confirmed | Dispatch compatibility is derived from mutable resource attributes, while execution validates immutable task metadata. Recovery tasks with empty metadata are grouped as `WeekendSubjects` after later resource updates and fail compatibility validation. |
| Permanent correction | Proposed | Awaiting approval of AC1-AC4. |
| Deployment/verification | Not started | Requires implementation, deployment, and production observation. |

## Incident evidence

- Observed on 2026-09-20 between 10:54 and 11:47 JST.
- Lambda reserved concurrency was 1; SQS showed 0 visible and 64 invisible messages with a 5,400-second visibility timeout.
- The task view showed 2 actual Running tasks during diagnosis, then 1, then 0. The reported 51 did not represent 51 simultaneous Lambda executions.
- CloudWatch Logs Insights over two hours found 88 Lambda invocations, 62 `CollectionDispatchCompatibilityException` messages, and one completion API `502 Bad Gateway`.
- Four Lambda code deployments occurred at 10:54, 11:04, 11:23, and 11:33 JST. These amplified churn but are not the compatibility defect itself.
- Example task `62a13b80-3197-4a3d-b7cf-6e03937b7602` was a Recovery task with empty immutable metadata. Its resource had later acquired `weekendPriorityUntil`, so the dispatcher classified it as `WeekendSubjects`; the leased task could not satisfy that key and the Lambda terminated before completing the attempt.
- Running attempts were eventually reclaimed as `LeaseExpired`; no queue, task, failure, or deployment state was mutated during diagnosis.

## Technical cause

`GetPendingDispatchesAsync` and `BuildExecutionEnvelopeAsync` deserialize `CollectionResourceEntity.AttributesJson`. That value is mutable and can be replaced by later requests. `CollectionPlatformWorkerClient.IsCompatible` validates `LeasedCollectionTask.Attributes`, which comes from the task's immutable metadata. These two sources can therefore produce different grouping keys for the same task.

The compatibility exception is thrown before the handler exception boundary. The attempt is not completed and Lambda exits abnormally, leaving the task Running until lease expiry. With batched execution this also prevents later tasks in the envelope from running.

## Concern and agreement ledger

| ID | Concern | Evidence and impact | Recommended disposition | Alternatives | Residual risk | AC/task/counterexample | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Mutable resource attributes can change dispatch grouping after task creation. | Confirmed by production task history and source trace. | Derive both initial and execution envelopes from immutable task metadata. | Remove compatibility validation. | Existing rows with empty metadata must remain valid Definition groups. | AC1/T1; Recovery task plus later weekend resource update. | Keep validation and unify the source. | Pending | Open decision |
| C2 | A compatibility invariant failure strands a lease and aborts the rest of a batch. | 62 occurrences and unfinished attempts were observed. | Reject/release the execution before task acquisition where possible; retain a defensive terminal path that never leaves an acquired attempt Running. | Treat it as an ordinary transient handler failure. | Silent retry loops must not hide invariant defects. | AC2/T2; mismatch before first task and after partial batch. | Fail safely and preserve diagnostics. | Pending | Open decision |
| C3 | Immediate queue mutation could discard diagnostic or valid wake messages. | 64 messages are invisible and wake messages are hints backed by DB outbox state. | Do not purge/delete; allow visibility and lease recovery, then verify durable redispatch after the fix. | Purge the queue or manually retry every task. | Recovery is slower until the 90-minute visibility timeout elapses. | AC3/T3; no destructive queue action. | Preserve evidence and data. | Pending | Open decision |
| C4 | Repeated deployments during backlog processing increase ambiguity and lease churn. | Four deployments occurred within 39 minutes. | Deployment verification must include a drained/observed window and no repeated redeploy during the production smoke check. | Continue rapid redeploys. | Unrelated API 502 may recur independently. | AC4/T4. | Use one controlled deployment and observation window. | Pending | Open decision |

## Acceptance criteria

| ID | Criterion | Task | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Dispatcher reservation, execution-envelope reconstruction, and worker validation derive compatibility from the same immutable task metadata and produce a Definition group for a Recovery task whose resource is later updated with weekend metadata. | T1 | Store/API/collector integration counterexample. | Not started |
| AC2 | A compatibility mismatch cannot leave an acquired attempt Running, cannot report successful batch completion, and does not prevent eligible work from being redispatched after lease/release recovery. | T2 | Mismatch, partial-batch, lease-expiry, and replay tests. | Not started |
| AC3 | Production recovery preserves task/failure history and does not purge SQS; after deployment, Running and invisible counts converge while at least one current race-detail task completes successfully. | T3 | Production API, SQS, Lambda, and task-history observation. | Not started |
| AC4 | Deployment is performed once under a recorded observation window; compatibility errors remain zero and no completion API 5xx occurs during the smoke period. | T4 | CloudWatch Logs Insights and Lambda/SQS metrics. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Unify dispatch compatibility on immutable task metadata. | Main | Lead | Approval | Collection store, dispatcher, focused tests | Dispatcher/store tests | Production-shaped Recovery/resource-update fixture passes. | Proposed |
| T2 | Make compatibility failures release or complete work without stranded attempts or aborted eligible batches. | Main | Lead | T1 | store/collector invocation boundary and tests | Collector and execution-lease tests | Mismatch/replay/partial-batch counterexamples pass. | Proposed |
| T3 | Deploy and observe non-destructive recovery. | Main | Lead | T1-T2 | production deployment and read-only verification | API/SQS/Lambda evidence | Backlog progresses, Running converges, race-detail succeeds. | Proposed |
| T4 | Close incident and document verification. | Main | Lead/Review | T3 | this record, read-only review | final review and validators | AC1-AC4 Verified with no open incident item. | Proposed |

## Review gates

- **Design and task-split review:** Compatibility and lease semantics cross dispatcher, persistence, Lambda invocation, and production recovery, so Lead retains implementation and integration. Focused read-only review may be delegated after approval.
- **Concern and agreement review:** C1-C4 are material and await user disposition. No destructive recovery is proposed.
- **Pre-implementation review:** Pending approval.
- **Checkpoint review:** Pending implementation.
- **Final review:** Requires code verification, one controlled deployment, and production observation.

## Documentation updates

- This incident record is the canonical source for the 2026-09-20 processing-count incident.
- No separate architecture document currently defines dispatch compatibility metadata; implementation will update one only if a canonical runtime document is found.

## Rollback

Roll back the code deployment if compatible tasks stop dispatching or queue depth grows after the smoke window. Do not purge the queue. Preserve DB outbox, attempts, failure notifications, and SQS/DLQ evidence for replay under the prior version.

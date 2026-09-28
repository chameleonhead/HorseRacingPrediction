# Production collection dispatch starvation

- Status: Proposed
- Change record schema: 2
- Owner: Collection platform maintainers
- Created: 2026-09-28
- Updated: 2026-09-28

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | No production code changed. A permanent dispatch fix is proposed below and awaits approval. |
| Verification | Not started | Production prevalence and affected outbox state remain unverified pending an authorized read-only SQLite aggregate. |
| Deployment/operation | Not started | No restart, pause/resume, retry, queue mutation, database repair, or deployment was performed. |

## Context

The production collection pipeline is not making meaningful progress. Operator-provided observations for the current incident are:

| Signal | Observation |
| --- | --- |
| Lambda platform | Invocations start and finish quickly; AWS `Errors = 0`. Lambda is not crashing at the platform level. |
| Deployed version | API and Lambda are a coordinated `ab1cda45` v1 pair. The current `main` v2 pair was not deployed because the deployment workflow's `verify` job failed. |
| Pipeline control | Unpaused. |
| Tasks | 3,718 Ready/waiting; 0 Running. |
| Actionable findings | 17 `ActionRequired`. |
| Race-detail Background lane | 1,833 arrived, 0 dispatched, 1,833 active. |
| Race-detail Normal lane | 1,630 arrived, 579 dispatched/completed, 1,253 active. |
| SQS (rolling samples) | Sent, received, and deleted continue at roughly 3–5 messages per 5 minutes. |
| Queue and DLQ | Instantaneous depth is zero. |

The counts and timings above were supplied by the operator in the incident handoff. Exact capture timestamps, metric periods, and the underlying AWS or production-console exports are not attached to this record. Failure-group details are auxiliary evidence; they do not by themselves explain the broad dispatch starvation.

No temporary recovery action has been performed. In particular, the dispatcher/API has not been restarted, the pipeline has not been paused or resumed, and no task, outbox row, queue message, or failure record has been retried, deleted, or repaired.

## Root-cause boundary and hypotheses

### Supported by repository source and production observations

The leading technical hypothesis is starvation between outbox eligibility/reservation and Lambda execution acquisition:

1. In deployed source `ab1cda45`, `CollectionPlatformStore.GetPendingDispatchesAsync` selects undispatched, available outbox rows whose reservation is absent or expired. It joins their task and resource, but does not require the task to be `Ready` or the outbox generation to equal the task's current dispatch generation.
2. The dispatcher reserves selected rows subject to `MaxInFlightEnvelopes`, sends a wake-only SQS message, and records the queue message ID. The reservation remains live until expiry unless the worker acquires the envelope.
3. `AcquireNextExecutionAsync` rejects an envelope if its task is no longer `Ready` or its generation is stale, returning `NoWork`. The Lambda can therefore acknowledge/delete a wake that made no task progress while its outbox reservation continues consuming the in-flight capacity slot for up to the configured reservation lifetime (reported as 45 seconds in this incident).
4. For wake-only delivery, `MarkWakeSentAsync` records the queue message ID but does not mark the row dispatched. `GetLaneDispatchStateAsync` derives persisted fairness only from rows with `DispatchedAt != null`; therefore these wake reservations do not advance the persisted lane state. The dispatcher advances a local copy only after a successful send, and that local state is discarded at the next polling iteration. Capacity rejection also leaves the selected lane unchanged during the current loop. Thus stale reservations can consume capacity without durable lane progression.
5. With limited `MaxInFlightEnvelopes`, repeatedly selecting stale/ineligible rows can consume dispatch capacity while leaving eligible tasks undispatched. The observed continuing SQS traffic and zero queue depth are consistent with wake delivery and acknowledgement, not proof of collector crashes.

Current `main` retains the same eligibility gap in `GetPendingDispatchesAsync`. It also changes the worker endpoint contract to `/api/v2/internal/collection/execution-leases`, where `NoWork` maps to HTTP 204, while the worker client unconditionally requires a JSON response. That v2 pair needs a consistent response contract before deployment. The deployed v1 API and Lambda are coordinated at `ab1cda45`; the v2 client mismatch is a future deployment blocker, not the demonstrated cause of the currently deployed v1 behavior.

### Not yet proven

The live database has not been inspected at outbox-row level. It is not yet established that stale or ineligible outbox rows are prevalent enough in production to explain the full 3,718-task backlog. A narrowly scoped, read-only SQLite aggregate is needed to count, without returning identifiers, undispatched rows by task status, generation match, reservation expiry, and lane, alongside eligible Ready/current-generation rows. Until that observation exists, stale-outbox starvation remains a strongly supported mechanism and the leading root-cause hypothesis, not a fully proven production prevalence claim.

The 17 actionable findings may contain isolated deterministic failures. They should not be bulk retried and are not presumed to account for the broad backlog.

## Goals

- Establish whether stale/ineligible outbox reservations are consuming dispatch capacity in production without exposing task, race, resource, or credential identifiers.
- Restore meaningful dispatch progress through the narrowest evidence-backed and reversible action.
- Correct the dispatcher eligibility and reservation handling so a `NoWork` acquisition cannot strand a `MaxInFlightEnvelopes` slot for the reservation timeout.
- Make lane fairness and dispatch/acquisition outcomes observable and alertable.
- Ensure API and Lambda contracts are verified and deployed as a coordinated pair.

## Non-goals

- Bulk deletion, bulk retry, or blanket regeneration of queued work.
- Clearing the SQS queue or DLQ to make depth metrics look healthy.
- Restarting, pausing, resuming, or repairing production before the current state is captured and the relevant action is authorized.
- Treating a successful Lambda invocation or empty queue as evidence of collection progress.
- Deploying current `main` before the v2 client contract and workflow route verification failures are resolved.

## Incident ledger

| Incident | Temporary recovery | Root cause | Corrective proposal | Permanent fix | Remaining risk |
| --- | --- | --- | --- | --- | --- |
| 2026-09-28; production collection reports no meaningful progress while Lambda invocations complete quickly, AWS Errors remain zero, and task backlog is 3,718 Ready / 0 Running | None performed | Likely stale/ineligible outbox reservation starvation between dispatcher selection and `AcquireNext`; production prevalence awaits read-only DB aggregate. Deployed code is v1 `ab1cda45`. | This Proposed record: read-only validation, narrow stabilization, permanent eligibility/lease/observability correction, and coordinated deployment prerequisites | Not started | Exact stale/current-generation distribution; whether a process restart is needed; 17 ActionRequired findings; deployment workflow verification blocker |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Production outbox rows and SQLite WAL state have not been inspected. The production API has no approved safe row-level diagnostic surface in the evidence supplied. | Acting on the hypothesis without validating prevalence could waste recovery effort or disturb unrelated work. | First authorize a strictly read-only SSH session to Lightsail to run an allowlisted aggregate against a consistent SQLite snapshot/backup; emit counts only, no identifiers or row contents. Confirm no database writes, no journal-mode changes, and a WAL-safe snapshot procedure before execution. | AC1/T1; counterexample: no stale/ineligible rows or stale rows are too few to explain reserved capacity. | Lead retains the database-access and privacy decision. A diagnostic count is necessary before choosing data repair. | Pending | Open decision |
| C2 | The correct stabilization depends on T1: SQS continues receiving/deleting messages, but progress and reservation state by lane are not yet correlated. | An unnecessary restart may interrupt useful work; repairing the wrong state may lose or duplicate work. | After T1, select one narrow branch: restart the API/dispatcher only if process health shows a stalled service; otherwise quarantine/repair only specific stale outbox/execution state using existing idempotent APIs after proving the target set. Observe a meaningful task transition before any further action. Never bulk-delete or bulk-retry. | AC2/T2; counterexample: dispatcher is healthy and current reservations are legitimate/in-flight. | No mutation is justified until the exact branch is chosen from evidence. | Pending | Open decision |
| C3 | `main` v2 worker client always parses JSON after acquire, while v2 endpoint returns 204 for `NoWork`. Deployment workflow also verifies v1 route usage in CI; the v2 deployment did not pass `verify`. | A coordinated cutover could convert ordinary `NoWork` into Lambda errors or deploy a broken pair. | Resolve the 204 response contract and update deployment verification to target the intended v2 routes. Deploy API and Lambda atomically as a coordinated version transition, with pause/drain, backups, health checks, resume guard, and rollback to the previous pair. Treat this as a deployment prerequisite, separate from the immediate incident recovery. | AC4/T4; counterexample: a v1 worker paired with v2 API or vice versa. | This is a material release blocker, not evidence of the current v1 incident trigger. | Pending | Resolved in design |
| C4 | The NoWork path must coexist with duplicate delivery, reservation expiry, Lambda timeout, API timeout, and concurrent dispatchers. | Eager release or capacity decrement could release a live reservation and permit duplicate execution. | Release only the exact envelope/reservation token after NoWork is classified as stale; make release idempotent and conditional. Retain timeout reclaim as crash recovery. Add concurrency and duplicate-delivery tests. | AC3/T3; counterexample: delayed valid acquire races with conditional release. | Release must be token-scoped and transactional; stale row filtering alone is insufficient if reservations can remain held. | Pending | Resolved in design |
| C5 | Reported operating counts lack raw metric exports and exact capture interval. | The impact timeline and progress rate cannot yet be independently reconstructed. | Preserve the operator-provided observations as attributed facts; collect timestamped before/after counts and queue/API/Lambda correlation during authorized diagnosis and recovery. | AC1–AC2/T1–T2 | No claim of independent telemetry verification is made. | Pending | Open decision |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC0 | This record preserves the operator-supplied incident observations, distinguishes them from source-verified facts and unverified production prevalence, and records that no temporary recovery or production mutation occurred. | T0 | Audit validator passes; source links/revisions and no-mutation boundary are present in this record. | Verified |
| AC1 | A read-only, identifier-free production aggregate reports counts of undispatched outbox rows by task status, generation match, reservation status, and lane, alongside eligible Ready/current-generation rows; its snapshot method is verified not to mutate production data or interfere with SQLite WAL. | T1 | Reviewed allowlisted SQL and snapshot plan; before/after DB integrity and WAL/file metadata checks as appropriate; output contains counts only. | Not started |
| AC2 | If a specific safe stabilization branch is justified by AC1/process evidence, the selected action is applied only to the allowlisted process or exact stale state, and timestamped observations show at least one meaningful eligible task progresses through dispatch and reaches a terminal state without immediate recurrence. If neither branch is justified, record why and keep the incident open. | T2 | Production progress/outbox/task counts and correlated API/Lambda/SQS observations over a stated window proportional to normal task duration. | Not started |
| AC3 | Dispatcher only reserves tasks that are currently Ready and whose dispatch generation matches the outbox row; a stale/ineligible NoWork envelope releases its exact reservation idempotently, frees capacity promptly, and does not allow duplicate execution or disturb a valid reservation. | T3 | Store/dispatcher regressions for stale generation, non-Ready task, reservation release, duplicate wake, delayed acquire race, expiry/reclaim, and multiple dispatchers; dispatcher-level fairness test proves Normal and Background lanes both progress under sustained load. | Not started |
| AC4 | Safe structured metrics/logs expose counts and transitions for eligible rows, reservations, sent wakes, acquire outcomes (including NoWork classification), reservation release/expiry, per-lane oldest age and dispatch completion; alarms detect repeated NoWork with reserved capacity and lane starvation. API/Lambda deployment contracts pass coordinated version tests and deployment verification targets the intended routes. | T4 | Contract tests cover both nonempty acquisition and no-work response; workflow-equivalent route verification passes; production cutover/rollback checklist and timestamped post-deploy progress evidence. | Not started |

## Delivery plan

1. Obtain explicit approval for C1's bounded production database diagnostic authority and the output restrictions. Use the existing SSH access only for one read-only Lightsail operation after approval; no API key, credentials, identifiers, or raw rows enter this record or shell transcript.
2. Establish a WAL-safe consistent snapshot or approved online-backup procedure. The exact allowlisted aggregate must be reviewed before execution. If the database is busy or safe snapshot consistency cannot be established, stop and report the blocker without changing the database.
3. Compare counts against the hypothesis. Keep the incident open if there are no stale/ineligible outbox rows or the observed capacity pattern does not fit.
4. Select the stabilization branch only after evidence: restart the API/dispatcher only if its process is demonstrably stalled; otherwise repair or quarantine only specifically identified stale reservation/execution state via existing idempotent APIs, after a narrowly scoped preview. Do not use bulk deletion or retry.
5. Observe meaningful progress before implementing or deploying the permanent fix. Label any successful temporary action `temporary recovery` and continue root-cause closure.
6. Implement the permanent dispatch correction and tests after the user approves this record. Fix current-main v2 204 parsing and workflow route verification as a separately tracked deployment prerequisite within this coordinated change.
7. Deploy API and Lambda together using the existing pause/drain and backup safeguards. Keep rollback available to the prior coordinated v1 pair. Resume only under the deployment guard, then observe meaningful progress through task completion and lane fairness.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T0 | Prepare this Proposed incident/change record from the supplied production observations and source comparison. | Documentation worker subagent | Worker; bounded documentation-only task; requested model telemetry unavailable | - | `docs/changes/20260928_production-collection-starvation/README.md`, `docs/changes/20260928_production-collection-starvation/agent-audits/T0-A1.json` | `python scripts/audit_agent_execution.py docs/changes/20260928_production-collection-starvation`; `git diff --check` | Proposed record and `agent-audits/T0-A1.json` | Verified | Worker — exclusive record ownership; model identity was not exposed at dispatch | T0-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T1 | Authorize and run exact read-only SQLite aggregate to validate production stale/ineligible outbox prevalence. | Lead for access/safety decision; bounded query preparation may be assigned to a Worker after frozen allowlist | Lead decision; Worker execution, `gpt-6-luna` / high after approval | User decision C1 | Read-only production DB; no writes; no IDs returned | Query review, WAL-safe consistent snapshot, aggregate output only, integrity and no-mutation evidence | Timestamped count-only diagnostic artifact; secrets and identifiers excluded | Dependent | Lead retains data-access/privacy choice; worker may execute only the frozen read-only query | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T2 | Choose and, if authorized, execute the narrow temporary recovery branch; observe meaningful progress. | Lead owns branch/risk decision; Worker may run exact read-only observation and approved bounded operation | Lead decision; Worker execution only after exact action approval | T1, user decision C2 | Explicitly allowlisted process or exact stale reservation/execution state only; no broad retries/deletes | Before/after state; idempotency/target preview; one meaningful task reaches terminal state; no immediate recurrence | Timestamped temporary-recovery note with affected counts and observation window | Dependent | Lead retains recovery decision because it affects production state; workers may execute only a frozen, reversible, bounded action | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T3 | Correct dispatch filtering, token-scoped reservation release/NoWork handling, and lane fairness; add counterexample regressions. | Lead for concurrency/persistence contract; coding worker for frozen implementation/tests | Lead design; Worker `gpt-6-luna` / high for bounded code/test slices after approval | T1/T2 evidence; user approves AC3 | Dispatcher/store and relevant tests only, exclusive ownership to be assigned at pre-implementation review | Focused store/dispatcher tests plus cross-process/duplicate-delivery regression; fairness assertion at dispatcher layer | Passing regression evidence and AC3 trace through dispatcher/acquire path | Dependent | Persistence/concurrency design remains Lead-owned; worker must return any contract change or race ambiguity | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T4 | Fix v2 no-work client/endpoint contract and route verification; deploy API and Lambda as an atomic coordinated pair and verify production progress/rollback readiness. | Lead owns rollout/rollback; worker can implement route/client correction under frozen API decision | Lead deployment decision; Worker `gpt-6-luna` / high for code/workflow and tests after approval | T3; exact workflow failure evidence; user approves AC4 | Worker/client contract, deployment workflow, contract tests; production rollout only under separately explicit approval | Contract tests for 201 and no-work behavior; workflow-equivalent route check; staged paired deployment and production observation | Passing CI/deploy checks, prior pair rollback target, task progress and lane fairness observations | Dependent | Lead retains external deployment and rollback; bounded source edits may be assigned after contract is frozen | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |

### Lead-owned decisions

- Exact SSH/database diagnostic authority, snapshot method, and allowlisted query: privacy and persistence safety decision; cannot be delegated before frozen boundaries.
- Whether the production evidence justifies a process restart or stale-state repair, and the exact target scope: operational risk decision.
- Reservation release semantics under concurrent acquisition/duplicate delivery: persistence and concurrency contract.
- API/Lambda cutover, pause/drain, rollback, and production acceptance: external state transition and final acceptance.

Worker slices are not yet dispatched. At dispatch, record the concrete requested model/agent and start revision in the audit; keep observed model, usage, and effort unavailable unless the runtime provides them. Coding workers must own exclusive paths, add/update the contracted tests, run the focused and handoff regressions, and return if a frozen decision changes or a verification gate fails.

## Documentation updates

- `docs/changes/20260928_production-collection-starvation/README.md`: this new incident-specific source of truth. No canonical architecture or operations document was changed because the dispatch defect and deployment prerequisite remain proposed and have not been implemented; inspect and update the relevant canonical collection platform design during the approved implementation.
- Repository source inspected: deployed `ab1cda45` store, dispatcher, worker client, and v1 endpoint; current `main` store, dispatcher, worker client, v2 endpoint, and deployment workflow. CodeGraph exploration was used before text search for current symbols.

## Technical impact

The incident affects queue-to-execution dispatch capacity and lane fairness. It does not currently show a Lambda platform failure: operator-provided AWS `Errors = 0`, short invocations, and continued SQS sends/receives/deletes indicate the wake path is running. Because queue depth is only an instantaneous snapshot, it does not show how many wake messages were acknowledged without work.

Source comparison:

- Deployed v1 at `ab1cda45`: `GetPendingDispatchesAsync` has the status/generation eligibility gap; the worker uses `/api/internal/collection/executions/acquire-next`; the endpoint returns a JSON result, including `NoWork`.
- Current `main` at `195cdb09cb481aa76314b1760611aac40805817c`: the same eligibility gap remains; the worker uses `/api/v2/internal/collection/execution-leases`; the endpoint returns 204 on NoWork although the client requires a JSON body.
- Deployment status: the operator reports that current `main` has not been deployed because workflow verification failed. Keep this failure separate from the current v1 runtime symptom until its exact failing check is attached.

## Decisions

- Record Lambda as operational at the platform level; investigate work starvation in the application dispatch path.
- Treat stale/ineligible outbox starvation as the leading mechanism but keep live production prevalence explicitly unproven pending the bounded aggregate.
- Preserve all queue, task, outbox, execution, and failure evidence. Never bulk-delete, clear, or retry to improve dashboard appearance.
- Keep temporary stabilization separate from permanent code correction and deployment acceptance.
- Deploy API and Lambda only as a coordinated, verified pair. Resolve the current-main v2 no-work response contract and workflow route check before that cutover.

## Design and task-split review

- **Lead review, 2026-09-28:** Proposed design preserves production data, distinguishes operator-supplied telemetry from repository evidence, and does not overstate stale-row prevalence. The count-only read diagnostic, conditional recovery branch, concurrency-safe code fix, contract prerequisite, rollout, and verification have distinct tasks and dependencies. T1/T2/T3/T4 are blocked on user approval and upstream evidence. No production work is runnable in this Proposed state.
- **Routing:** T0 was a short bounded documentation slice delegated to one exclusive writer. T1 query execution can be worker-executed only after the Lead freezes exact SQL, snapshot procedure, and output controls. T3/T4 mechanical code and test slices may use Luna/high only after the Lead approves the persistence/API and deployment contracts. The Lead keeps database access, concurrent reservation semantics, recovery, rollout, and final acceptance because these decisions cross privacy, durability, concurrency, and external-state boundaries.
- **Independent review:** Not dispatched; approval is pending, and there is no implementation to review. After approval, Lead performs the integrated AC-group review and an independent review is warranted for reservation concurrency and the paired production cutover.

## Verification record

- `codegraph explore "Outbox GetPendingDispatches AcquireNext NoWork MaxInFlight reservation 45 seconds collection dispatcher"` and `codegraph explore "CollectionDispatchOutboxEntity CollectionTaskEntity CollectionLane OutboxReservationSeconds MaxInFlightEnvelopes SQLite mapping table name"` — confirmed current-main query, dispatcher, acquire endpoint, reservation counting, queue send marking, and fairness-state derivation.
- `git show ab1cda45:<path>` source comparison — confirmed deployed v1 uses the same `GetPendingDispatchesAsync` status/generation gap and v1 JSON acquire-next endpoint; deployed and current-main route/response differences are recorded above.
- `git rev-parse HEAD` and `git rev-parse origin/main` — both `195cdb09cb481aa76314b1760611aac40805817c` at record creation.
- Production observations in Context — operator supplied; not independently fetched in this documentation-only task.
- No production operation, administration API call, AWS mutation, SSH session, restart, retry, or deployment was performed.
- `python scripts/audit_agent_execution.py docs/changes/20260928_production-collection-starvation` — passed (`README.md: valid`).
- `git diff --check` and `git diff --cached --check` — passed before the documentation commit.
- `python scripts/validate_change_records.py ...` — attempted per DDD's repeated-audit note, but this repository does not contain `scripts/validate_change_records.py`; `rg --files` confirmed it is absent. The required agent-audit validator is present and passed.
- Final tracked/untracked scope review — only this change-record directory was staged; post-commit `git status --short` was clean.

## Deviations and follow-up

- Before approval, the user must decide C1 and C2: allow or reject the narrowly scoped Lightsail read-only SQLite aggregate, and accept the staged conditional stabilization design. Approval must cover AC1–AC4 and the stated concern dispositions; it does not authorize unlisted database writes, bulk retries/deletes, or deployment.
- If T1 disproves the stale-row prevalence hypothesis, revise the root-cause proposal before implementation approval. Inspect the exact workflow `verify` failure and record the failing check before the paired deployment is proposed as runnable.
- After implementation, add concrete test results, exact change/deployment identifiers, rollback evidence, and timestamped progress observation. Do not mark the record Implemented until the original production symptom is traced through meaningful task completion and each approved AC is verified.

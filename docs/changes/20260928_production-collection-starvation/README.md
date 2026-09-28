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
| Deployed version | API and Lambda are a coordinated `ab1cda45` v1 pair. The current `main` v2 pair was not deployed. The reported `verify` run failed at `Verify isolated API repair and cross-process locks`; the exact failing output is not attached and its cause is unverified. |
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

Current `main` retains the same eligibility gap in `GetPendingDispatchesAsync`. An earlier draft incorrectly said the v2 endpoint returns HTTP 204 on `NoWork`. The store method returns a non-nullable `CollectionExecutionAcquireResult` on every branch, including `NoWork`; the endpoint's `lease is null`/204 branch is therefore unreachable, and current behavior serializes the result as HTTP 201 JSON. The worker's JSON parsing is consistent with this implementation. This is a non-causal implementation/spec drift because the route ledger expects 204; reconcile the contract and add end-to-end coverage, but do not treat it as a production or CI blocker. The actual current-main cutover risk is the deployment workflow's use of v1 admin paths after deploying the v2 route surface, described under C3 and T4. The failed `verify` step is separate and its cause is unknown.

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
| C1 | Production outbox rows and SQLite WAL state have not been inspected. No approved safe row-level diagnostic surface is evidenced. | Running an underspecified query or copying only the main SQLite file could expose identifiers, observe an inconsistent snapshot, disturb WAL behavior, or produce misleading counts. | Before any SSH connection, prepare a reviewable diagnostic artifact containing the exact read-only SQL; SQLite open mode and WAL-safe snapshot method; exact production host command and allowed output fields; hard timeout; a no-write proof (query-only/read-only settings and post-check); rollback action or explicit `not applicable`; and independent review. Output is limited to timestamp/window plus aggregate counts, including Ready tasks with no matching current-generation undispatched outbox and task/outbox cardinality anomalies; no IDs or row contents. The user must separately authorize execution after reviewing this artifact. | AC1/T1; counterexamples: no stale rows, missing outbox counts are zero, or task/outbox anomalies do not explain occupied capacity. | Lead retains query, host, privacy, and WAL-safety review. No SSH operation is authorized by this Proposed record. | Pending | Open decision |
| C2 | The correct stabilization depends on T1: SQS continues receiving/deleting messages, while progress and reservation state by lane are not correlated. No operator-facing API is evidenced for releasing/quarantining one exact reservation; `ReleaseUnstartedDispatchesAsync` is private store logic, not an operator surface. | An unnecessary restart may interrupt useful work; direct database repair risks data loss or duplicate work. Continued queue receives/deletes argue against assuming a stalled dispatcher. | Restart the API/dispatcher only if process-health evidence demonstrates a stall. If T1 confirms stale/ineligible reselection while the process is healthy, temporary reservation repair is unavailable; proceed with a reviewed code fix and later separately authorized deployment. Reservation expiry alone is not sufficient recovery. Any direct data repair requires a separate destructive-change design and explicit approval and is excluded here. | AC2/T2; counterexample: process is healthy and stale-state concentration is absent, so restart would not address the failure. | No production mutation is currently justified. Prefer no temporary recovery over unsupported DB edits; keep the incident open until meaningful progress follows a verified fix. | Pending | Open decision |
| C3 | `.github/workflows/app-deploy.yml` contains v1 admin path literals under `/api/admin/collection`: pre-deploy GET pipeline, POST pause, and task drain; post-deploy GET pipeline/tasks/failure-notifications and POST resume. Current `main` exposes the corresponding v2 routes under `/api/v2/admin/collection`, including `GET /pipeline-state`, `GET /tasks`, `GET /failure-notifications`, and `PUT /pipeline`. Pre-deploy calls run while v1 is active; post-deploy calls would still use v1 after a v2-only cutover. | A v2-only cutover may fail post-deploy verification/resume and leave the pipeline paused. The separately reported `verify` failure at `Verify isolated API repair and cross-process locks` has no attached output; no route-related cause is established. | Update/test post-deploy calls for v2 or explicitly retain tested v1 compatibility routes. Keep pre-deploy v1 pause/drain. Diagnose the earlier verify-step output separately; do not attribute it to routes. This is a future cutover prerequisite, not the current incident cause or reported verification failure. | AC5/T5; counterexample: pre-deploy succeeds against v1 but post-deploy state checks target old paths on v2. | Workflow literals and current v2 routes are source-verified; the reported CI failure cause remains unknown. | Pending | Resolved in design |
| C4 | Current v2 endpoint implementation returns `201` with JSON `CollectionExecutionAcquireResult` for `NoWork`; `AcquireNextExecutionAsync` returns a non-nullable result on every branch, so the endpoint's null/204 branch is unreachable. The versioned route ledger expects `204`. | This is non-causal implementation/spec drift. It does not explain deployed v1 starvation or the reported CI step, but an undocumented contract can affect future consumers. | Reconcile the intended route contract with the ledger, preserve the explicitly selected response, and add a real HTTP-to-worker-client test covering status, body, and deserialization. Do not describe current JSON behavior as a production or CI blocker. | AC4/T4; counterexample: direct store tests pass while HTTP serialization/client handling disagrees. | The prior draft's 204 mismatch claim is retracted. API contract selection should be reviewed before code changes. | Pending | Open decision |
| C5 | The NoWork path must coexist with duplicate delivery, reservation expiry, Lambda timeout, API timeout, and concurrent dispatchers. | Eager release or capacity decrement could release a live reservation and permit duplicate execution. | Release only the exact envelope/reservation token after NoWork is classified as stale; make release idempotent and conditional. Retain timeout reclaim as crash recovery. Add concurrency and duplicate-delivery tests. | AC3/T3; counterexample: delayed valid acquire races with conditional release. | Release must be token-scoped and transactional; stale row filtering alone is insufficient if reservations can remain held. | Pending | Resolved in design |
| C6 | Reported operating counts lack raw metric exports and exact capture interval. | The impact timeline and progress rate cannot yet be independently reconstructed. | Preserve operator observations as attributed facts; collect timestamped before/after counts and queue/API/Lambda correlation during authorized diagnosis and recovery. | AC1–AC2/T1–T2 | No claim of independent telemetry verification is made. | Pending | Open decision |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC0 | This record preserves the operator-supplied incident observations, distinguishes them from source-verified facts and unverified production prevalence, records that no temporary recovery occurred, and retains review corrections without rewriting the earlier audit evidence. | T0, T6 | Audit validator passes; source comparisons and correction history are recorded; no production mutation occurred. | Verified |
| AC1 | A timestamped read-only, identifier-free production aggregate reports counts of undispatched outbox rows by task status, generation match, reservation status, and lane; Ready tasks with no matching current-generation undispatched outbox; and task/outbox cardinality anomalies. A pre-execution artifact specifies exact SQL, SQLite open mode, WAL-safe snapshot, host command, allowed output, timeout, no-write proof, rollback/not-applicable, and independent review. | T1 | User reviews the artifact and separately authorizes SSH execution; query-only connection and WAL-safe snapshot are independently checked; before/after no-write evidence; output includes observation timestamp/window and counts only. | Not started |
| AC2 | Restart the API/dispatcher only if process-health evidence shows a stall and the restart is separately authorized; verify meaningful eligible work progresses. If process health is normal and stale outbox state is confirmed, perform no ad-hoc DB repair: record temporary recovery as unavailable and keep the incident open for the reviewed code fix and later authorized deployment. | T2 | Timestamped pre/post process, task and lane counts; a meaningful terminal task after an authorized restart, or an explicit no-mutation decision with code/deployment tasks still open. | Not started |
| AC3 | Dispatcher only selects rows for currently Ready tasks at the current dispatch generation and transactionally revalidates both predicates while reserving capacity. A stale/ineligible `NoWork` envelope releases only its exact reservation idempotently, frees capacity promptly, and cannot duplicate execution or disturb valid reservations. | T3 | Store/dispatcher tests include task becoming terminal and generation rotating between selection and reservation, duplicate wake, delayed acquire race, expiry/reclaim, multiple dispatchers, and lane fairness under sustained load. | Not started |
| AC4 | Structured metrics/logs expose eligible rows, reservations, sent wakes, acquire outcomes including JSON `NoWork`, reservation release/expiry, per-lane oldest age and dispatch completion; alarms detect repeated `NoWork` with reserved capacity and lane starvation. The v2 acquire behavior is reconciled with the route ledger and verified end-to-end without misclassifying current 201 JSON behavior as the production incident. | T4 | Real HTTP/API plus worker-client test covers acquired and `NoWork` response, status, body, and deserialization; selected route contract is reconciled with the versioned ledger. | Not started |
| AC5 | After source, tests, image plan, and rollback target are reviewed, a separately and explicitly authorized deployment moves API and Lambda together; post-deploy checks use the active v2 routes, rollback remains viable, and collection makes meaningful progress. | T5 | Later explicit deployment authorization; recorded image/API pair, pause/drain and backup evidence, route checks, rollback plan, timestamped task completion and lane-progress observation. | Not started |

## Delivery plan

1. Obtain explicit approval for C1's bounded production database diagnostic authority and the output restrictions. Use the existing SSH access only for one read-only Lightsail operation after approval; no API key, credentials, identifiers, or raw rows enter this record or shell transcript.
2. Before any SSH session, prepare the diagnostic artifact containing exact SQL, WAL-safe open/snapshot mode, exact host command and output allowlist, timeout, no-write proof, rollback/not-applicable, and independent review. The artifact must show only a timestamp/window and aggregate counts. The user must separately authorize execution after reviewing it. If a safe snapshot cannot be established, stop without touching production state.
3. Compare counts against the hypothesis. Keep the incident open if there are no stale/ineligible outbox rows or the observed capacity pattern does not fit.
4. Select the stabilization branch only after evidence: restart API/dispatcher only if process health demonstrates a stall and the user explicitly authorizes the restart. If process health is normal and stale outbox state is confirmed, do not edit the database; no operator-facing exact reservation release API exists. Record temporary recovery as unavailable pending the reviewed code fix. Waiting for reservation expiry alone is not considered enough.
5. Implement the permanent dispatch correction and tests after this design is approved. Correct transactionally checked eligibility and conditional reservation release. Reconcile the v2 response contract/spec drift and diagnose the reported `Verify isolated API repair and cross-process locks` failure from its actual output; the latter's cause is currently unknown.
6. Update and test post-deploy workflow calls for the v2 routes (or maintain explicit v1 compatibility routes). Keep v1 pre-deploy pause/drain behavior for the currently deployed API.
7. Prepare a separate deployment package with exact API/Lambda image identifiers, build and verification results, pre/post route checks, backup evidence, pause/drain plan, rollback target, and expected observation window.
8. Obtain separate explicit production authorization after the deployment package and image/rollback plan are reviewed. Approval of this record or its implementation does not authorize production cutover.
9. Deploy API and Lambda together only after that authorization, then observe task completion and lane fairness.

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T0 | Prepare this Proposed incident/change record from the supplied production observations and source comparison. | Documentation worker subagent | Worker; bounded documentation-only task; requested model telemetry unavailable | - | `docs/changes/20260928_production-collection-starvation/README.md`, `docs/changes/20260928_production-collection-starvation/agent-audits/T0-A1.json` | `python scripts/audit_agent_execution.py docs/changes/20260928_production-collection-starvation`; `git diff --check` | Proposed record and `agent-audits/T0-A1.json` | Verified | Worker — exclusive record ownership; model identity was not exposed at dispatch | T0-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T1 | Prepare the diagnostic artifact and, only after separate user authorization, run the exact read-only SQLite aggregate to validate production prevalence. | Lead for data/privacy contract and independent review; bounded query drafting may be assigned to a Worker | Lead safety review; Worker execution, `gpt-6-luna` / high only against frozen artifact | User decision C1; artifact review precedes SSH execution | `docs/changes/20260928_production-collection-starvation/diagnostics/read-only-production-aggregate.md`; read-only production DB with no writes or identifiers | Review exact SQL, SQLite open/snapshot mode, host command/output allowlist, timeout, no-write proof, rollback/N/A; after separate authorization, timestamped counts and no-write checks | Independently reviewed artifact and authorized count-only diagnostic result | Dependent | Lead retains host/WAL/privacy approval; worker may prepare or execute only the exact reviewed read-only contract and only after separate execution authorization | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T2 | Decide whether a process restart is justified; perform it only if process-health evidence shows a stall and separate explicit authorization is granted. If health is normal and stale outbox rows dominate, record temporary recovery as unavailable and keep the incident open for the code fix. | Lead incident lead | Lead — final acceptance and external recovery decision | T1; user decision C2 | No production mutation unless exact restart is separately authorized; no DB row updates, reservation release, retry, or deletion | Timestamped process-health evidence before restart; if restarted, verify meaningful task completion and no immediate recurrence; otherwise record no-mutation rationale | Temporary recovery observation or explicit no-temporary-recovery record | Dependent | Lead — final acceptance; restart is an external state mutation and no worker can infer approval | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T3 | Correct dispatch eligibility transactionally, release only the exact stale reservation on NoWork, and preserve fairness; add race regressions. | Lead for concurrency/persistence contract; coding worker for frozen implementation/tests | Lead design; Worker `gpt-6-luna` / high for bounded code/test slices after approval | T1/T2 evidence; user approves AC3 | Dispatcher/store and relevant tests only, exclusive ownership assigned at pre-implementation review | Tests for stale generation/status at selection and changes between selection/reservation; terminal transition; duplicate wake; delayed acquire race; expiry/reclaim; multiple dispatchers; lane fairness | Passing regression evidence and AC3 trace through dispatcher/reserve/acquire path | Dependent | Persistence/concurrency semantics remain Lead-owned; worker returns any race or contract ambiguity | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T4 | Reconcile v2 JSON 201 versus ledger-intended 204, add real HTTP-to-client contract tests, correct post-deploy route usage, and diagnose the reported verify-step failure from its actual output. | Lead for API contract choice; coding worker for frozen endpoint/client/workflow/test changes | Lead public-contract decision; Worker `gpt-6-luna` / high for bounded source/tests after approval | User resolves C4; T3; verify-step output | Acquire endpoint/client, route ledger, `.github/workflows/app-deploy.yml`, and focused tests only; no production deployment | Test HTTP status/body/deserialization across API and client; workflow test for v1 pre-deploy and v2 post-deploy routes; separately reproduce exact verify failure | Passing contract and workflow tests; confirmed verify failure cause or evidence-based remaining blocker | Dependent | Lead retains public contract choice; worker returns if ledger contract or route compatibility requires a scope change | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T5 | After implementation is verified, prepare the deployment package; deploy only after later explicit authorization. | Lead incident/release owner | Lead — final acceptance | T3/T4 verified; separate user deployment authorization after image and rollback plan review | Production API/Lambda deployment and associated pause/drain/resume only when separately authorized | Approved image identifiers, complete CI, pause/drain and backup evidence, v2 route checks, rollback plan, observation window, meaningful task/lane progress | Change/deployment identifiers, healthy coordinated pair, rollback readiness, timestamped terminal task progress | Dependent | Lead — final acceptance; production deployment is an external state transition | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |
| T6 | Correct the independent-review finding in this record while preserving T0's original audit artifact. | Documentation worker subagent | Worker; bounded corrective documentation and audit task; model telemetry unavailable | Review finding F1 | `docs/changes/20260928_production-collection-starvation/README.md`, `docs/changes/20260928_production-collection-starvation/agent-audits/T6-A1.json` | `python scripts/audit_agent_execution.py docs/changes/20260928_production-collection-starvation`; `git diff --check` | F1 closed with current source evidence; original T0-A1 JSON unchanged; T6-A1 records corrective patch | Verified | Worker — exclusive proposed-record ownership; lead independently checks source contract and accepted correction | T6-A1 | unavailable; retries 0; corrections 0; reviews 1 |

### Lead-owned decisions

- Exact SSH/database diagnostic authority, snapshot method, and allowlisted query: privacy and persistence safety decision; cannot be delegated before frozen boundaries.
- Whether process-health evidence justifies a restart: operational risk decision. No operator API exists to repair an exact outbox reservation; direct DB mutation is excluded and requires a separate destructive-change design and approval.
- Reservation release semantics under concurrent acquisition/duplicate delivery: persistence and concurrency contract.
- Intended HTTP 201 JSON versus ledger-intended 204 for v2 `NoWork`: versioned API contract decision.
- API/Lambda cutover, pause/drain, rollback, and production acceptance: external state transition and final acceptance; later explicit deployment authorization required.

Worker slices are not yet dispatched. At dispatch, record the concrete requested model/agent and start revision in the audit; keep observed model, usage, and effort unavailable unless the runtime provides them. Coding workers must own exclusive paths, add/update the contracted tests, run the focused and handoff regressions, and return if a frozen decision changes or a verification gate fails.

## Documentation updates

- `docs/changes/20260928_production-collection-starvation/README.md`: this new incident-specific source of truth. No canonical architecture or operations document was changed because the dispatch defect and deployment prerequisite remain proposed and have not been implemented; inspect and update the relevant canonical collection platform design during the approved implementation.
- Repository source inspected: deployed `ab1cda45` store, dispatcher, worker client, and v1 endpoint; current `main` store, dispatcher, worker client, v2 endpoint, and deployment workflow. CodeGraph exploration was used before text search for current symbols.

## Technical impact

The incident affects queue-to-execution dispatch capacity and lane fairness. It does not currently show a Lambda platform failure: operator-provided AWS `Errors = 0`, short invocations, and continued SQS sends/receives/deletes indicate the wake path is running. Because queue depth is only an instantaneous snapshot, it does not show how many wake messages were acknowledged without work.

Source comparison:

- Deployed v1 at `ab1cda45`: `GetPendingDispatchesAsync` has the status/generation eligibility gap; the worker uses `/api/internal/collection/executions/acquire-next`; the endpoint returns a JSON result, including `NoWork`.
- Current `main` at `195cdb09cb481aa76314b1760611aac40805817c`: the same eligibility gap remains; the worker uses `/api/v2/internal/collection/execution-leases`; the store returns `CollectionExecutionAcquireResult` even on `NoWork`, and the endpoint serializes it as HTTP 201 JSON. The conditional null/204 branch is unreachable; JSON parsing matches the implementation. The route ledger expects 204, so reconcile and test the spec drift; it is not the deployed v1 cause or the reported CI failure.
- Current-main workflow transition: `app-deploy.yml` contains v1 `/api/admin/collection/...` paths before and after deployment. Pre-deploy pause/drain runs against the currently active v1 API and is valid; post-deploy checks/resume need v2 routes or retained v1 compatibility routes after a v2-only cutover.
- Deployment status: current `main` was not deployed. The reported run failed at `Verify isolated API repair and cross-process locks`; exact output and cause are unknown and require separate diagnosis. Do not associate it with either response spec drift or post-deploy route compatibility without evidence.

## Decisions

- Record Lambda as operational at the platform level; investigate work starvation in the application dispatch path.
- Treat stale/ineligible outbox starvation as the leading mechanism but keep live production prevalence explicitly unproven pending the bounded aggregate.
- Preserve all queue, task, outbox, execution, and failure evidence. Never bulk-delete, clear, or retry to improve dashboard appearance.
- Keep temporary stabilization separate from permanent code correction and deployment acceptance.
- Deploy API and Lambda only as a coordinated, verified pair. Reconcile the response contract and post-deploy routes first. This record does not authorize cutover; obtain separate explicit production authorization after tests and image/rollback plan review.

## Design and task-split review

- **Lead review, 2026-09-28:** Proposed design preserves production data, distinguishes operator-supplied telemetry from repository evidence, and does not overstate stale-row prevalence. T1 requires an independently reviewed exact diagnostic artifact before any separately authorized SSH execution. T2 proposes no direct DB repair because no operator release API exists; restart requires process-stall evidence. AC3 requires transactional reservation-time validation and task-state/generation race tests. Contract reconciliation, source/workflow changes, and production deployment are separate tasks; deployment needs later explicit authorization. No production work is runnable in this Proposed state.
- **Routing:** T0/T6 are bounded documentation slices with exclusive scope. T1 artifact drafting may be delegated after output constraints freeze; Lead independently reviews before SSH authorization. T3/T4 code/test slices may use Luna/high only after Lead settles persistence/API contracts. The Lead keeps database/host access, concurrent reservation semantics, recovery choice, rollout, and final acceptance because these decisions cross privacy, durability, concurrency, and external-state boundaries. T5 is Lead-owned and remains unapproved pending separate deployment authorization.
- **Independent review:** The independent reviewer identified F1 before user approval; T6-A1 records the correction and closes the finding against current source. There is no implementation to review. After approval, Lead performs the integrated AC-group review, with independent review warranted for reservation concurrency and the coordinated production cutover.

## Verification record

- `codegraph explore "Outbox GetPendingDispatches AcquireNext NoWork MaxInFlight reservation 45 seconds collection dispatcher"` and `codegraph explore "CollectionDispatchOutboxEntity CollectionTaskEntity CollectionLane OutboxReservationSeconds MaxInFlightEnvelopes SQLite mapping table name"` — confirmed current-main query, dispatcher, acquire endpoint, reservation counting, queue send marking, and fairness-state derivation.
- `git show ab1cda45:<path>` source comparison — confirmed deployed v1 uses the same `GetPendingDispatchesAsync` status/generation gap and v1 JSON acquire-next endpoint; deployed and current-main route/response differences are recorded above.
- Current-main source check — `AcquireNextExecutionAsync` has a non-nullable `CollectionExecutionAcquireResult` return type; all `NoWork` paths return a result object. `AcquireNextExecutionEndpoint` serializes that object with HTTP 201; its null/204 branch is unreachable. Worker JSON parsing agrees with current behavior; the versioned route ledger's 204 expectation remains spec drift pending reconciliation and end-to-end coverage.
- `.github/workflows/app-deploy.yml` source check — pre-deploy and post-deploy steps use v1 `/api/admin/collection/...` paths. Pre-deploy pause/drain targets the still-active v1 API; post-deploy verification/resume must target v2 or rely on deliberately retained compatibility endpoints.
- Current-main deploy run — reported failure at `Verify isolated API repair and cross-process locks`. The exact output and cause are not available in the evidence inspected; no route or acquire-contract cause is claimed.
- `git rev-parse HEAD` and `git rev-parse origin/main` — both `195cdb09cb481aa76314b1760611aac40805817c` at record creation.
- Production observations in Context — operator supplied; not independently fetched in this documentation-only task.
- No production operation, administration API call, AWS mutation, SSH session, restart, retry, or deployment was performed.
- `python scripts/audit_agent_execution.py docs/changes/20260928_production-collection-starvation` — passed (`README.md: valid`) before and after the F1 correction.
- `git diff --check` and `git diff --cached --check` — passed before the documentation commit.
- `python scripts/validate_change_records.py ...` — attempted per DDD's repeated-audit note, but this repository does not contain `scripts/validate_change_records.py`; `rg --files` confirmed it is absent. The required agent-audit validator is present and passed.
- Final tracked/untracked scope review — original T0-A1 JSON remains unchanged; the correction is limited to this README and T6-A1; only this change-record directory is in scope.

## Review correction history

- **F1 — corrected by T6-A1:** The initial draft claimed current-main's acquire endpoint returns HTTP 204 on `NoWork` and mismatches the worker's JSON reader. That claim was incorrect: `AcquireNextExecutionAsync` returns a non-nullable `CollectionExecutionAcquireResult`, every `NoWork` branch returns an instance, and the endpoint serializes that instance as HTTP 201 JSON. The null/204 branch is unreachable; the worker's JSON parse matches the current implementation. A distinct implementation/spec drift remains because the versioned route ledger expects 204. It is non-causal for the deployed v1 incident and the reported `verify` failure; reconcile the contract and add an HTTP-to-worker-client test. T0-A1's original audit artifact remains unchanged; this follow-up and its evidence are captured in T6-A1.
- **Separate v2 cutover finding:** Source confirms `.github/workflows/app-deploy.yml` uses v1 `/api/admin/collection/...` paths in both the pre-deploy pause/drain and post-deploy state-check/resume steps. The pre-deploy calls are valid while the v1 API is still active. After a v2-only API cutover, the post-deploy steps must use the v2 routes or intentionally retained v1 compatibility endpoints. The reported run failed at `Verify isolated API repair and cross-process locks`; its underlying output/cause is unavailable and is not attributed to route or response-contract behavior.

## Verification failure ledger

| ID | Finding | Evidence and impact | Task | State |
| --- | --- | --- | --- | --- |
| F1 | Incorrect v2 `NoWork` 204/client mismatch assertion in initial proposal | Non-nullable store result and endpoint source show HTTP 201 JSON. The false assertion could direct work toward a nonexistent current Lambda failure; corrected while the separate route-ledger contract drift remains tracked. | T6 | Verified |

## Deviations and follow-up

- Before approval, the user must decide C1/C2/C4: accept preparation and later separate review/authorization for the exact read-only Lightsail aggregate; accept that restart is conditional on stall evidence while ad-hoc DB repair is excluded; and reconcile the v2 JSON 201 implementation with route-ledger 204 expectation. Approval does not authorize SSH execution, direct DB writes, restart, bulk retry/delete, or deployment.
- If T1 disproves the stale-row prevalence hypothesis, revise the root-cause proposal before implementation. Inspect the exact command/output behind the reported `Verify isolated API repair and cross-process locks` failure; the step name is known, but its failure cause remains unverified.
- T4 source/test approval is distinct from T5 production deployment. T5 requires a later explicit authorization after exact image IDs, CI evidence, route transition, backup/pause/drain sequence, and rollback package are reviewable.
- After implementation, add concrete test results, exact change/deployment identifiers, rollback evidence, and timestamped progress observation. Do not mark the record Implemented until the original production symptom is traced through meaningful task completion and each approved AC is verified.

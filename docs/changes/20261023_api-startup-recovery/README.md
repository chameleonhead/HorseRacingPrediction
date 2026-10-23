# 起動ブロック中のAPIを安全に復旧配備する

- Status: Approved
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-23
- Updated: 2026-10-24

Approval2026-10-24: user「本番の復旧をお願いします」in direct response to the summarized recovery proposal authorizes AC1–4 and C1–4 dispositions: explicit pause/tasks/batch checks,900s/API stop/backup/API-only exactimage restore, no newToken/AWSlogin/Lambda change or auto resume. Parent original3migration/retry/separateresume remains approved and incomplete. No reset or guard bypass is authorized.

## Context and outcome

Parent: [offline migration admission](../20261021_offline-migration-admission/README.md), P5/P5D. Source338abfdf includes the locally verified legacy-journal startup repair. Native2057pass/8skip and original Windows API786pass/14skip/0fail close local gates, not production recovery. Current deployed d306 failed readiness; normal app-deploy first requires a collection API response and therefore cannot reach the repaired API while that API is blocked. Local SSH publickey was rejected. Workflow-owned deployment credentials remain available; no new Token or local AWS login is the proposed requirement.

Add a narrowly scoped **manual API startup recovery mode** to the existing app-deploy workflow. It restores the API only, leaves collection paused, then the already approved parent migration/retry/separate-resume work continues. This is not a data migration, arbitrary remote-command UI, or a replacement for normal deployment guards.

## Proposed sequence and boundaries

1. Manual dispatch only, default normal mode unchanged. Require an expected current deployed commit SHA and validate both current and target image identities. Reject missing/malformed inputs or non-manual activation before external writes. Retain the production environment and the existing single deployment concurrency group.
2. Keep canonical verify/build/publish gates. Recovery does not run Terraform, update Lambda/code/queue configuration, reuse an old run's pause output, overwrite old SHA tags, or expose deployment credentials.
3. Before stop, verify the expected current API image and its exact authenticated migration-blocked503 signal. Ordinary healthy API, unavailable network, auth error, unexpected body/state or revision mismatch is not permission to run recovery.
4. Stop the normal API through existing SSH/Compose control and confirm its process is stopped. Independently read persisted collection state in SQLite read-only mode: exactly one explicit pipeline control row must have IsPaused=1, collection_tasks Running must be zero, and collection_execution_leases StartPending/Running must both be zero. Missing rows/tables, unknown/malformed states or failed integrity checks abort. Do not instantiate CollectionPlatformStore (its constructor migrates schema). No initializer, schema repair, task cancellation, pause-row rewrite or guessed default is allowed in this inspection.
5. Keep the API stopped for at least900 seconds after the successful explicit pause/work/lease checks, then repeat all checks. Exclude another local API/writer through the existing ownership protocol, with a real Linux cross-process contention test against the API before release; preserve gates and never delete a lock to make the check pass. Hold exclusion through backup and hand it off under serialized deployment control immediately before normal API startup. This extra bounded quiescence is explicit for this recovery mode, not a claim that normal deployment already waits900 seconds.
6. Run existing journal preflight and preserve all configured databases, the operational journal, marker and required sidecars in a verified backup before allowing new-version startup. Unknown/partial/active journal, unknown source paths, inconsistent backup or concurrent ownership stops the operation. No domain record is rewritten by recovery helpers.
7. Start only the exact verified new API image. Normal startup may perform only its already approved additive operational-journal upgrade. Require actual Swagger200 plus authenticated paused/Running0 readback; /health200 alone is not readiness. Failure leaves collection stopped and reports failure, not success or automatic retry/resume. Retain backup and safe metadata for diagnosis.

Inputs are deployment revisions/mode only. No business horse identifiers, resource presets, arbitrary SQL/shell, credentials or file paths are accepted. The previously approved original3 horse mappings remain separate operator-selected migration input.

## Workflow contract inventory

| Entry/input | Disposition | Target/invariants |
| --- | --- | --- |
| push main/path-filter deployment | Unchanged | Normal deployment only; cannot activate recovery |
| workflow_dispatch with default inputs | Unchanged | Normal deployment/guards |
| workflow_dispatch horse_id_migration_operation_id | Unchanged | Existing strictGUID migration route; mutually exclusive with recovery |
| workflow_dispatch api_startup_recovery | New boolean/defaultfalse | Explicit API-only manual route; no automatic resume/Lambda/Terraform branch |
| workflow_dispatch expected_deployed_sha | New bounded40hex string | Required only with recovery; must equal independently inspected current image; reject invalid/conflicting input before external writes |

Inventory5:3 unchanged,2 new; no route/input deletion or reinterpretation. No HTTP/admin UI contract is changed. Workflow tests must reconcile exact entries and mutually exclusive branches, not merely string presence.

## Hypothesis review

| Claim | Evidence versus inference | Falsifier / gate | Disposition |
| --- | --- | --- | --- |
| Ordinary new push can restore this blocked API | Workflow deploy depends on collector job; its first pipeline-state curl fails on503; minimal host collection fallback503 is traced | app-deploy169–170/479–497; RuntimeMode253–305 | Falsified; no blind push/retry |
| Old successful job output proves current pause | Old run39524808602 recorded paused=true, but that is a historical snapshot | No independent current persisted-state read | Rejected as current proof; no rollback dispatched |
| Current persisted pause/Running state is safe | Unknown; prior deployment and blocked-host elapsed time are not substituted for current state | Read-only offline guard must establish true/zero twice or abort | Runtime prerequisite, never an assumed migration premise |
| Deployed local data writers and backup membership are identifiable | P5D source inventory: compose only API mounts /data; Lambda worker and persistence use HTTP; existing rendered-config resolver covers3stores, optional localqueue, journal and caller marker. SQS is remote, not a SQLite snapshot member | Own all resolved local sources, fail on unknown paths/schema/owners; Linux contention interop remains mandatory implementation evidence | Resolved design boundary; never claim remote queue empty or current production state verified |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | API unavailable cannot provide ordinary pause readback | An HTTP-error bypass could deploy into unknown state | Independent read-only persisted pause/Running checks; exact blocked-state eligibility, no default values | AC1/AC2,R1/R2;503/auth/malformed/Running counterexamples | Require fail-closed proof | Approved24Oct | Resolved in design |
| C2 | Single deployed local API owns mounted SQLite; Lambda writes HTTP only, but delayed invocations and held source gates remain possible | Unsafe copy or incompatible restart | Include active batch leases,900s after explicit drained state, complete configured local source set and existing ownership/preflight/backup; require real contention proof; abort unknown/held state. SQS contents are untouched/not claimed backed up | AC2,R1/R2;held owner/unknownpath/corruptjournal/activebatch | Agree with guarded design; require release gates | Approved24Oct | Resolved in design |
| C3 | Recovery requires downtime and may refuse unsafe state | Still unavailable after a failed guard | Explicit900s stopped interval, finite bounds/metadata, no resume/reset/forced cleanup; reconsider through diagnosis if guard fails | AC2/AC3,R2/R4 | Recommend bounded maintenance | Approved24Oct | Resolved in design |
| C4 | Mode accidentally changes normal deployment or Lambda | Broader unapproved rollout | Manual-only default-off mode/DAG isolation; normal semantic guard regressions and unchanged Lambda configuration | AC1/AC4,R2/R3 | Keep scope API-only | Approved24Oct | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Only explicit manual recovery of the expected blocked image is eligible; normal deploy unchanged, no new credentials/Lambda/Terraform changes | R1,R2,R3 | Parsed workflow/input/DAG tests; mismatch/auth/healthy/nonmanual rejection | Not started |
| AC2 | Fresh read-only explicit pause, zero Running tasks and zero StartPending/Running batch leases, complete writer exclusion,900s recheck and verified full configured local backup precede startup; unknown/held/partial states abort without business writes | R1,R2,R3 | Real SQLite fixtures/read-only unchanged hashes, actual Linux API/guard lock contention, held-owner/sidecar/journal/timeout/order counterexamples | Not started |
| AC3 | Exact new image starts with normal readiness and collection still paused; failure remains stopped, no automatic migration/retry/resume; backup and diagnostics retained | R2,R3,R4 | Controlled process success/failure and actual matching-SHA production health/pause evidence | Not started |
| AC4 | Attributable source passes exact workflow-native gates and original regressions, then parent recovery resumes without falsely closing its migration acceptance | R2,R3,R4 | Native formatter/build/tests/guards, matching remote run, parent CURRENT/AC reconciliation | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| R1 | Freeze operational safety design after exact source inventory and user approval | Lead; P5D Luna read-only evidence | Lead | Parent P5D | This record/canonical deployment docs only | Hypothesis/concern/AC review; canonical validator | P5D evidence and24Oct user approval; no open design concern | Verified | Lead — architecture/persistence/security/approval | none | unavailable; retries 0; corrections 0; reviews 1 |
| R2 | Implement bounded manual recovery guard and workflow wiring | pending_planner | Luna/high | R1 | Four files frozen in R2 readiness | Smallest persisted-state/sequence counterexamples first, existing deploy guard regressions and actual API lock interop beforehandoff | Plan accepted; narrow edit release after canonical audit0 | In progress | Worker — frozen bounded operational implementation after approval | R2-A1 | unavailable; retries 0; corrections 0; reviews 0 |
| R3 | Independent native integrated verification | Planned Luna verifier pending_admission_executor | Luna/high | R2 | Read-only source; disposable outputs only | Exact CI formatter/build/fulltests/guards and real isolated recovery success/failure path | Not dispatched | Dependent | Worker — independent mechanical gate evidence | none | unavailable; retries 0; corrections 0; reviews 0 |
| R4 | Deploy recovery mode, verify actual restored API, return to parent operation | Lead | Lead | R3 | Canonical GitHub deployment and authorized production checks/records only | Matching run/image/backup/health/paused evidence, then parent original3migration task | Not started | Dependent | Lead — production/data/security/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

## Review gates and CURRENT

R2 pre-edit readiness24Oct: current skills loaded, existing Luna/high followup accepted; observedmodel/usage unavailable, not inferred. Cheap plan uses existing deploy helper, no new service/CLI/framework. Exclusive writes: `.github/workflows/app-deploy.yml`, `deploy/horse-id-migration-deploy.sh`, `tests/scripts/test-deploy-migration-guard.ps1`, plus `tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationRuntimeModeTests.cs` only if required for actual helper/API cross-process Linux ownership proof. Existing helpers resolve complete rendered configuration, readonly journal/integrity, backup exactset/sidecars/hash; new mode uses explicit pause/tasks/batchleases, known-state eligibility and failure-stop. First smallest deliverable is actual lock interoperability on isolated real API: .NET-held gate rejects helper beforecopy; actual helper-held sameinode gate blocks API; release permits normal Swagger. No guessed lock substitution, missinggate reconstruction or injected test bypass into production guards. Then minimal workflow DAG/inputs and full remote sequence/negativecases; exact callback/helper layout remains internal freedom. No unresolved publicspec question; unknown safety/platform result returnsLead without weakening AC. Focused native `pwsh tests/scripts/test-deploy-migration-guard.ps1 -Bash /bin/bash` and `pwsh tests/scripts/test-deploy-pipeline-state.ps1 -Bash /bin/bash` must0; related new API Linux test build+run mustexecute/pass, Windows intentionalplatformskip allowed. R3 then final native full commands once, including source-dependent original Windows API if C# test input changed. All commands bounded/noninteractive; no worker commit/push/prod. Canonical audit0 before first edit.

Execution Mode after24Oct approval. Main reloaded orchestration/DDD/incident skills and required references. R1 Verified; R2 In progress/pre-edit planning, R3/R4 Dependent. Existing Luna/high planner is reused; no source edit until repository-informed exact scope/commands/ownership plan and canonical audit0. Source338abfdf remains locally verified/notpushed; current normaldeploy still requires blockedAPI, so no blind push. No rollback/guard bypass/migration/retry/resume. Preserve unrelated dirtyskills/AGENTS/policytests/routing/efficiency records. Next R2 plan→Lead security/persistence refinement→edit release→small focused proof→native finalgates→checkpoint/push/explicitrecoverydispatch→actualhealthy pausedAPI→parent original3recovery. No additional ledger or broadgate rerun before a coherent finalpatch.

R2 editrelease after plan/readiness/audit0. R4 publishing plan: adding the manual route on main necessarily creates an ordinary push-triggered app-deploy run, which is currently ineligible because the old API is blocked. Lead will identify only that exact newSHA/event=push app-deploy run and cancel it during verification before any production action, retain its logs as intentionally cancelled (not a passing deployment), keep sameSHA app-ci running, then explicitly dispatch the approved recovery mode at that SHA. Never cancel unrelated runs or use old run outputs. Required remote deployment proof is the matching manual recovery run including its verify/build/backup/ownership/readiness branch; intentional normal/collector branch exclusion must remain explicit. If the automatic run has unexpectedly advanced, inspect actual steps before any subsequent dispatch.

R1 source evidence: collection controls schema Migrator105–109, DbContext164–167 and Store2820–2825; task status text mapping DbContext104–110; batch lease schema Migrator329–342 and active status guards Store2077–2085/2400–2408. Collector worker HTTP Acquire/Start/Complete42–180 and HttpDataCollectionWriteService20–39; Program14min cancellation/15min platform bound152–158. Existing deployment resolver89–168, journal preflight283–350, exact-file backup469–532 and sidecar check534–547. Guard must use rendered configuration paths, mode=ro/query_only and explicit data, not application initialization or historical outputs. Current production state and cross-language/source lock interoperability are not inferred from these source facts; release tests and actual offline guard must establish them.

## Documentation updates

`docs/00-system-architecture.md` deployment section links this separately proposed manual recovery boundary; normal deployed architecture remains unchanged. Parent record keeps actual recovery unfinished. No admin UI change is part of this startup-recovery proposal.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | In progress | Approved; R2 editrelease after plan/audit0 |
| Verification | Not started | P5D source coverage/design review recorded; implementation/native/production tests remain unperformed |
| Deployment/operation | Not started | Current API remains blocked; no recovery dispatch |

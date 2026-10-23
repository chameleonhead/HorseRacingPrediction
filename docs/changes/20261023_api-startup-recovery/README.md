# 起動ブロック中のAPIを安全に復旧配備する

- Status: Proposed
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-23
- Updated: 2026-10-23

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
| C1 | API unavailable cannot provide ordinary pause readback | An HTTP-error bypass could deploy into unknown state | Independent read-only persisted pause/Running checks; exact blocked-state eligibility, no default values | AC1/AC2,R1/R2;503/auth/malformed/Running counterexamples | Require fail-closed proof | Pending | Resolved in design |
| C2 | Single deployed local API owns mounted SQLite; Lambda writes HTTP only, but delayed invocations and held source gates remain possible | Unsafe copy or incompatible restart | Include active batch leases,900s after explicit drained state, complete configured local source set and existing ownership/preflight/backup; require real contention proof; abort unknown/held state. SQS contents are untouched/not claimed backed up | AC2,R1/R2;held owner/unknownpath/corruptjournal/activebatch | Require these release gates; no architecture objection remains | Pending | Resolved in design |
| C3 | Recovery requires downtime and may refuse unsafe state | Still unavailable after a failed guard | Explicit900s stopped interval, finite bounds/metadata, no resume/reset/forced cleanup; reconsider through diagnosis if guard fails | AC2/AC3,R2/R4 | Recommend bounded maintenance | Pending | Resolved in design |
| C4 | Mode accidentally changes normal deployment or Lambda | Broader unapproved rollout | Manual-only default-off mode/DAG isolation; normal semantic guard regressions and unchanged Lambda configuration | AC1/AC4,R2/R3 | Keep scope API-only | Pending | Resolved in design |

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
| R1 | Freeze operational safety design after exact source inventory and user approval | Lead; P5D Luna read-only evidence | Lead | Parent P5D | This record/canonical deployment docs only | Hypothesis/concern/AC review; canonical validator | P5D source inventory accepted; user approval pending | In progress | Lead — architecture/persistence/security/approval | none | unavailable; retries 0; corrections 0; reviews 1 |
| R2 | Implement bounded manual recovery guard and workflow wiring | Planned Luna executor pending_planner | Luna/high | R1 approval | .github/workflows/app-deploy.yml; one deploy recovery helper; focused tests/scripts guard fixtures only, exact list frozen beforeedit | Smallest persisted-state/sequence counterexamples first, existing deploy guard regressions beforehandoff | Not dispatched | Dependent | Worker — frozen bounded operational implementation after approval | none | unavailable; retries 0; corrections 0; reviews 0 |
| R3 | Independent native integrated verification | Planned Luna verifier pending_admission_executor | Luna/high | R2 | Read-only source; disposable outputs only | Exact CI formatter/build/fulltests/guards and real isolated recovery success/failure path | Not dispatched | Dependent | Worker — independent mechanical gate evidence | none | unavailable; retries 0; corrections 0; reviews 0 |
| R4 | Deploy recovery mode, verify actual restored API, return to parent operation | Lead | Lead | R3 | Canonical GitHub deployment and authorized production checks/records only | Matching run/image/backup/health/paused evidence, then parent original3migration task | Not started | Dependent | Lead — production/data/security/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

## Review gates and CURRENT

Design Mode. No implementation or new production action authorized by this Proposed record. Lead design/task-split and concern review: source inventory closes design uncertainty, all ACs have task/counterexample coverage, scopes serialize, local implementation gates and actual production prerequisites remain explicitly unverified. No open material design decision remains; next is concise user approval of AC1–4/C1–4. Source338abfdf is committed but not pushed; normal push would hit the known blocked API guard. No rollback/guard bypass/migration/retry/resume was performed. User dirt remains unchanged. Beforeedit readiness must freeze exact helper/test names and commands and validate audit. An unsafe condition is a return-to-Lead boundary, never a reason to weaken tests.

R1 source evidence: collection controls schema Migrator105–109, DbContext164–167 and Store2820–2825; task status text mapping DbContext104–110; batch lease schema Migrator329–342 and active status guards Store2077–2085/2400–2408. Collector worker HTTP Acquire/Start/Complete42–180 and HttpDataCollectionWriteService20–39; Program14min cancellation/15min platform bound152–158. Existing deployment resolver89–168, journal preflight283–350, exact-file backup469–532 and sidecar check534–547. Guard must use rendered configuration paths, mode=ro/query_only and explicit data, not application initialization or historical outputs. Current production state and cross-language/source lock interoperability are not inferred from these source facts; release tests and actual offline guard must establish them.

## Documentation updates

`docs/00-system-architecture.md` deployment section links this separately proposed manual recovery boundary; normal deployed architecture remains unchanged. Parent record keeps actual recovery unfinished. No admin UI change is part of this startup-recovery proposal.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | Proposed only; no helper/workflow edits |
| Verification | Not started | P5D source coverage/design review recorded; implementation/native/production tests remain unperformed |
| Deployment/operation | Not started | Current API remains blocked; no recovery dispatch |

# 中断状態を保持したまま保護用画面だけを更新する限定配備

- Status: Approved
- Change record schema: 2
- Owner: Lead
- Created: 2026-10-25
- Updated: 2026-10-25

## CURRENT

Approved / Execution Mode; operator-only image update, no migration/resume/reset/gate removal. N2-A2 (5.6 Luna/high) corrects the existing three-file bootstrap slice; native verification follows its frozen diff. Independent contract review found distinct operation/intent hashes and timezone-less JST persistence, both masked by synthetic fixtures; fix the real contract and counterexamples before dispatch. T6-A2 verifies the final application in the owned Linux runner with short native TMPDIR; supervisor-script freeze is coordinated with T3-A2. No production mutation or push yet. Next: native app/process gates, native bootstrap gates, safe-ref publication, explicit limited deployment/readback. Parent41migration remains unfinished.

## Evidence and decision

Lead read existing app-deploy.yml recovery and normal paths plus horse-id-migration-deploy.sh: both require normal pipeline/Swagger readiness; recovery cleanup stops the API on failure. Fresh25Oct01:00UTC public probes: /health200, /login200, /races503 with exact `Application startup is blocked by migration state.`; unauthenticated exact known operation status401. Swagger404 is not normal readiness. Read-only GH latest run metadata remains24Oct40070389580/source2872aaf3, not proof of exact current container identity. Do not blindly run either current deployment mode.

Add one explicit manual operator-UI-only mode to the existing canonical workflow, with a specific operation GUID and exact expected deployed SHA. Validate actual container/source identity, persisted operation/current gate binding and collection pause before mutation; reject mismatch/unknown/concurrent writer. Stop and confirm the owned container/children fully exited, hold source/journal exclusivity, retain existing900second drain recheck, and create a complete backup of configured stores, journal, initialization marker and maintenance markers without altering their contents. Replace only the API image with the verified error-containment image. No fixed migration CLI, alias migration, journal status reclassification, marker deletion or collection resume.

New image must start in journal-only protected mode from the preserved bound intent/gates. Validate exact running image, liveness, authenticated existing operation status and authenticated operator HTML availability; verify ordinary business API remains unavailable. This is operator-screen restoration, not normal API/data recovery. If new image cannot be proven protected, do not enable business access or weaken readiness. Preserve backup/operation and report failure for controlled recovery; no automatic data rollback. Failure cleanup must not erase evidence or call a data-writing migration. Full container stop means a bounded handoff interruption is possible; no redundancy is added.

## Finite workflow contract ledger

| workflow_dispatch input | Disposition | Invariant |
| --- | --- | --- |
| horse_id_migration_operation_id | Unchanged, forbidden with operator mode | Never invokes offline migration to implement operator bootstrap |
| api_startup_recovery | Unchanged, mutually exclusive | Existing normal-API recovery behavior/safety/readiness remains |
| expected_deployed_sha | Existing exact identity check reused | Required40lowercasehex; mismatch aborts before stop |
| operator_ui_recovery_operation_id | New optional GUID selecting operator-only mode | Manual only, nonempty canonicalGUID, bound to persisted current operation/gates; no generic bypass |

4inputs=3existing+1new; zero new application HTTP routes. Normal push/manual deployment, Lambda deployment and existing recovery/migration branches retain their previous semantics; operator-only mode skips unrelated Lambda rollout and uses existing production concurrency/environment credentials. Exact job predicates and parsed shell paths require tests, not string-only acceptance.

## Concern and agreement ledger

| ID | Evidence/impact | Disposition/alternative and residual risk | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Normal Swagger cannot be readiness for intentional protected mode; calling it normal success is misleading | Separate explicit operator-only readiness and result label. Alternative wait for external manual repair, leaving approved UI undeployed | AC1/AC3,N2/N4 | Support narrow bootstrap, never weaken normal readiness | Approved25Oct「お願いします」; boundary restated | Resolved in design |
| C2 | Active intent/source ownership could hide writer or partial data; generic redeploy unsafe | Exact image/GUID/gates plus full exit/exclusive leases/pause/backup; no data action or gate clearing. Stop on unknown/mismatch | AC2/AC4,N2/N3/N4 | No reset, replay, force-unlock or backend migration | Approved25Oct same boundary | Resolved in design |
| C3 | Wrong job predicates/helper branch can execute unrelated rollout or expose credentials | Mutual-exclusive manual inputs, same concurrency/production secrets, real parsed-shell failure fixtures, no secrets in args/output/artifacts | AC1/AC4,N2/N3 | Require native semantic tests before dispatch | Approved25Oct same boundary | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Manual exact-image/operation mode only; all existing deployment modes remain unchanged, invalid/conflicting inputs rejected before side effects | N1,N2,N3,N4 | Parsed workflow/helper exact branch semantic tests and matching run inputs | Not started |
| AC2 | Stop/drain/exclusive/full backup precedes image replacement; domain data and maintenance ownership are preserved | N1,N2,N3,N4 | Real command simulation+native process/file assertions and production receipts | Not started |
| AC3 | New image serves authenticated operator/error UI while business writes stay blocked; result explicitly not full recovery | N1,N2,N3,N4 | Exact SHA/status/HTML/API checks, auth negative controls | Not started |
| AC4 | No migration CLI/retry/resume/reset/marker deletion, secret leakage or duplicate execution on failure | N1,N2,N3,N4 | Forbidden-call/failure/restart/input counterexamples and actual run logs | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| N1 | Establish deployment mismatch and freeze bounded scope | Lead | Lead | none | This record; read-only workflow/helper/public probes | Exact source guard and public response distinction | Above source/probe evidence; no dispatch | Verified | Lead — operational authority/architecture/security | none | unavailable; retries 0; corrections 0; reviews 0 |
| N2 | Implement explicit limited bootstrap mode | bootstrap_luna_fallback | Luna/high | Approval, frozen protected-host contract | .github/workflows/app-deploy.yml, deploy/horse-id-migration-deploy.sh, corresponding deployment guard tests only | Exact native parsed-shell/input/failure branches, no unrelated action | 6quota beforedispatch;5.6/highselected and read-only plan completed; Lead internal guard decisions below beforeedit | In progress | Worker — bounded exclusive workflow/helper slice | N2-A1, N2-A2 | unavailable; retries 0; corrections 0; reviews 0 |
| N3 | Verify attributable frozen helper/workflow | preparation_luna_fallback | Luna/high | N2 | Owned native temporary artifacts, no source edits | Ruby/Python backend parity where applicable, native semantic guard/original-negative control and exactCI | Preliminary review confirmed config-ID vs registry-digest defect and missing executable lifecycle proof; awaiting corrected freeze | Dependent | Worker — mechanical independent validation | N3-A1 | unavailable; retries 0; corrections 0; reviews 0 |
| N4 | Deploy and verify operator-only restoration | Lead | Lead | N2,N3, approved application tests | Explicit canonical workflow dispatch and production readback | Full matching run through terminal, backup/ownership/image/operator-page/business-block receipts | No deployment yet | Dependent | Lead — production credentials/data/final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |

## Documentation updates and review

Publication preflight25Oct (read-only): `gh workflow run --help` documents `--ref` as the branch/tag containing the workflow version to run; workflow277460632 is active on default branch. Production environment has no deployment branch policy or protection rule. Fetched origin/main=a705b333; local basee5a9def2 is three documentation commits ahead, no divergent source. After native gates and purpose-scoped commits, publish a `codex/` branch and manually dispatch that exact ref with operator-only GUID/expected oldSHA, avoiding main's automatic normal deploy. The workflow itself must freshly verify actual container image/source before any stop; prior run metadata is not that proof. This publication does not authorize migration or resume.

Lead pre-deployment review corrections remain local closure items: the running supervisor holds its control lease for its lifetime, so readonly binding/pause checks precede stop, while exclusive source/journal locks follow confirmed exit and precede backup. A Docker config image ID is not a registry manifest digest; restore the captured local image identity without pulling a fabricated repo@config-ID. The pause producer is EF SetPausedAsync, not necessarily JstTime.ToDatabaseString: readonly current native fixture returned `2026-10-25 19:13:38.2334411` (space separator). Real producer fixtures must cover that persisted representation, age/future rejection and unchanged pause identity. Execute the parsed lifecycle and cleanup failure paths; string-order checks alone are not acceptance. No production action performed.

Lead internal plan decisions25Oct: use a separate bounded read-only intent↔immutable-gate↔configured-source binding validator and a preserve-existing-gates lease path exclusively in the explicit operator branch; never weaken existing preflight/lease rejection for other modes. Reuse existing canonical path/hash/version rules, reject unsupported formats, and test actual C#-produced gate fixtures to prevent duplicate-contract drift. Validate stable pause age>=900s and zero running before stopping the old host, then recheck the same state under leases; abort before stop if not satisfied, no unconditional900s offline wait. An ephemeral Compose override sets the existing operator-only flag for this job only; canonical Compose/domain configuration stays unchanged. T3 hardens that existing private flag to truly force the root and child into read-only operator mode even with Ready/unknown journal; no apply/verify transition in this mode. Immutable maintenance gates/init/domain/journal data are preserved; dynamic lease/capability records retain their existing renewal semantics. Unknown/mismatched bindings abort, never delete/rewrite gates. Failure cleanup must not stop an already-proven protected host solely for a later diagnostic failure; no automatic data rollback. Lead reviews exact failure behavior and target-image/source identity before live dispatch. New manual mode has no Lambda/normal-deployment side effects; safe publication/ref path is checked before pushing main so normal automatic deployment is not blindly triggered against the blocked site.

Readiness25Oct: orchestration/DDD/incident loaded by Lead. Frozen purpose is exact-operation/old-image-bound operator-only image update while preserving all data/intents/gates. Worker first inspects existing workflow/helper/read-only preflight/backup/locking/tests and returns exact reusable functions, files/ordered steps/remaining material questions/semantic commands. Lead retains image/source/ownership/security decisions. No speculative service/token/new HTTP route, no generic bypass of readiness. Existing modes remain unchanged; actual source may reveal an internal implementation decision but must not silently weaken guard/backup coverage. This approval does not permit dispatch until source/runtime and native semantic gates pass. Audit validation required before implementation.

Operational extension only; link from parent error-containment T5. No canonical behavior changes until approval; after approval update canonical operational/UI description and exact workflow input docs. Existing application design stays approved and independent implementation continues. Lead design/task-split/concern review finds no need for an additional server, token or AWS login; existing workflow supplies its own production credentials. No implementation write gate released; readiness/failure semantic commands and exact native versions must be recorded at dispatch. Parent41migration acceptance is not excluded by this bootstrap milestone.

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | In progress | Approved bootstrap implementation exists; contract and cleanup corrections remain |
| Verification | In progress | Local syntax/parsed semantic checks passed; final native and real-writer compatibility gates remain |
| Deployment/operation | Not started | No dispatch or server mutation |

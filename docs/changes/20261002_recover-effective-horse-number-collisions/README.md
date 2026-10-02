# Effective horse number collision recovery

Status: Implemented

## Completion summary

| Dimension | State | Evidence |
|---|---|---|
| Code | Implemented | Legacy Horse identity enrichment and stable JRA horse-route normalization are deployed. |
| Verification | Verified | Focused/full suites, build, format, CodeGraph, CI, deployment, production recovery, and idempotent replay passed as recorded below. |
| Deployment/operation | Verified | The pipeline is running; all eight target tasks succeeded; no failure group remains; all 48 races on 2026-09-26/27 are payout-declared with winning-horse and result timestamps. |

## Incident summary

JRA result collection reaches the bulk result API, but the API rejects the complete envelope with
`InvalidHorseNumber: Effective horse numbers must be unique; no data was written.`. Because the
write is atomic, `RaceResultDeclared` is not emitted and races whose official results are already
published remain `CardPublished` (shown as 出馬表公開) instead of 結果確定.

The current `main` revision `ca5dbedb` passed both `app-ci` and `app-deploy`; this is not an
undeployed-parser-fix symptom.

## Evidence and root cause

- `JraRaceCardCollectionWorkflow.ValidateCardEntriesForPersistence` validates positive and unique
  non-null horse numbers in the outgoing card. It cannot see existing persisted assignments.
- `RaceIdentityValidationService.ValidateCollectedEntryIdentities` computes the effective set by
  combining existing entries with identities resolved from the result page. It rejects the entire
  envelope when two effective entries resolve to one horse number.
- The error text reported by the operator is emitted only by that effective-set validation.
- The result workflow sends a winning horse and result entries in one atomic request. The rejection
  therefore prevents the lifecycle transition to `ResultDeclared`, explaining the stale status for
  2026-09-26 and 2026-09-27.
- The repository already provides a fenced entry-assignment repair workflow with a collection hold,
  official source snapshot, version/fingerprint checks, atomic repair event, and release/recollection.

The affected race IDs and the exact conflicting old/new Horse IDs still require read-only production
diagnostics before mutation. Replaying the failed task before repair is intentionally prohibited
because this is a deterministic validation failure.

The later failure for `20201206:Chukyo:11` is the same identity-cutover boundary at an earlier stage.
Its production attempt failed at `2026-10-02 05:57:40 JST` with
`HorseIdentityEvidenceRequired`. The resource was discovered from the horse named `ゴールドドリーム`,
has no persisted race detail, and was requested from the official JRA race page. The API rejects an
official source identity when the only same-name record is a source-less, name-derived legacy Horse ID.
This prevents a second Horse record from being created, but currently provides no safe promotion path.

## Frozen decisions

1. Do not weaken or bypass the effective-number uniqueness guard.
2. Do not infer a horse number from text following `ブリンカー着用`; that remains an unsupported
   hypothesis.
3. Repair persisted entry assignments only from an official JRA card snapshot with source identity,
   under the existing race repair hold and optimistic-concurrency checks.
4. Recollect results only after the repaired assignment fingerprint is verified.
5. A result is considered recovered only when both persisted entry/result evidence and lifecycle
   status are verified; task success alone is insufficient.
6. When an official JRA horse identity arrives and there is exactly one normalized-name match whose
   Horse ID is provably name-derived and whose source identity is empty, preserve that Horse ID and
   attach the official source identity through the existing profile write path. This is identity
   enrichment, not a merge or a new Horse registration.
7. Do not apply decision 6 when an official/source-bound Horse already matches, when more than one
   normalized-name candidate exists, when birth-date evidence conflicts, or when the candidate ID is
   not provably name-derived. Those cases remain blocked for explicit repair.
8. If both a legacy Horse and a separate official-identity Horse already exist, keep automatic
   resolution fail-closed and use the fenced assignment/identity repair workflow before recollection.

## Material concerns

| Concern | Evidence / impact | Recommended handling | Alternative | Residual risk | State |
|---|---|---|---|---|---|
| Automatic overwrite could attach results to the wrong horse | The API detected an existing/incoming identity conflict | Preserve the guard and use fenced official-snapshot repair | Accept incoming result as authoritative | JRA source changes between inspection and apply; mitigated by snapshot hash/version checks | Resolved in design |
| Blind retry cannot succeed | Validation failure is deterministic and writes nothing | Diagnose, repair, then create one controlled recollection | Retry the failure group now | Repeated notifications and unchanged race status | Resolved in design |
| Scope of affected races is not yet known | Operator identified dates, not race IDs/group key | Query actionable failures and race evidence read-only before repair | Assume every race on both dates is corrupt | Unnecessary repair surface | Resolved in design |
| Existing source data may already contain duplicate persisted assignments | Effective-set validation includes untouched existing entries | Inspection must distinguish stale identity alias from pre-existing duplicate number | Relax uniqueness | Silent corruption | Resolved in design |
| Every runner has a different stored/result Horse ID | Controlled production recollection reports 11/11 to 16/16 collisions in each affected race, not an isolated number shift | Treat this as legacy identity cutover, never as a horse-number correction. Enrich a unique source-less name-derived record only when no official record exists; otherwise require fenced repair | Replace entries by horse number alone | Same-name horses remain blocked whenever uniqueness or provenance cannot be proven | Resolved in design |
| Name equality alone can conflate different horses | `20201206:Chukyo:11` reached `HorseIdentityEvidenceRequired`; the current guard intentionally refuses name-only promotion | Require exact normalized name, exactly one provably name-derived/source-less candidate, no official/source-bound match, and compatible birth date when present | Always prefer the old record, or always create the official-ID record | A historical namesake without birth evidence remains possible; ambiguity stays blocked and is surfaced for repair | Resolved in design |
| Identity enrichment and profile persistence are separate writes | Resolver selection happens before the race envelope; source identity is persisted by the existing profile collection path | Make recollection success require source identity read-back; retry remains idempotent and a crash before profile persistence leaves the same safe candidate | Rewrite Horse aggregate identity during race ingestion | A transient failure can leave an enriched race reference with profile work pending, but cannot create a second Horse or silently merge records | Resolved in design |
| JRA exposes the same horse through multiple CNAME route families | Production read-back for `ゴールドドリーム` showed `pw01dud00.../DD` while the official result supplied `pw01dud10.../C4`; whole-CNAME comparison incorrectly raised `HorseIdentityConflict` | Normalize recognized JRA horse routes to the stable ten-digit horse number while preserving the navigable URL; retain legacy behavior for unrecognized forms | Add route-family aliases one by one | A future JRA route without the stable number remains fail-closed under the legacy identity | Resolved in design |
| The eight 2026 recoveries can be delayed by the upstream site independently of this defect | Post-deployment controlled recovery no longer returned `InvalidHorseNumber`; all eight entered automatic retry after JRA access-limit or transient-server responses | Leave the pipeline running and allow bounded automatic backoff; do not amplify the upstream limit with repeated manual retries | Cancel and recreate as realtime work | Result persistence remains externally pending, so AC-06/07 and T-04/05 stay incomplete | Externally blocked |

## Hypothesis ledger

| Claim | Fact / inference boundary | Supporting and contradicting evidence | Falsification | Result | Disposition |
|---|---|---|---|---|---|
| The 2020 failure is caused by the legacy-to-official Horse identity boundary | Fact: production error is `HorseIdentityEvidenceRequired`; fact: the resolver emits it for source-less name-derived conflicts. Inference: the subject is the discovery horse until per-subject evidence is read back | The job was discovered from `ゴールドドリーム`; no race data was written. The group UI does not expose the subject name without the technical detail expander | Reproduce with a production-shaped official-source request against one source-less name-derived horse and inspect the structured subject failure | The existing resolver test and source path reproduce the terminal code; production subject read-back remains a pre-mutation verification | Design valid; operation remains gated by preview |
| A unique source-less name-derived record can be reused without changing its Horse ID | Fact: `LoadHorsesAsync` proves name-derived IDs from registration history; fact: profile writes accept a previously empty matching source identity. Inference: this closes the 2020 path | Current code deliberately blocks this candidate at `CollectionIdentityResolver.ResolveHorse`; profile endpoint rejects conflicting but not empty source identity | Integration test race ingestion, profile persistence, read-back, repeat ingestion, and conflicting-source counterexample | Isolated code paths support the design; integrated test is required before deployment | Resolved in design |
| The eight 2026 collisions need repair rather than resolver fallback | Fact: controlled recollection found distinct existing and incoming Horse IDs for every runner | An already-created official Horse causes the resolver to select it, leaving the legacy RaceEntry collision intact | Preview each race and verify both identities before any apply | Production evidence already shows both IDs; exact repair preview is still required | Repair remains fenced |

## Acceptance criteria

| ID | Criterion | State |
|---|---|---|
| AC-01 | Read-only diagnostics identify every affected 2026-09-26/27 race, its failure/task IDs, existing assignments, incoming official assignments, and the exact collision without exposing secrets. | Verified |
| AC-02 | The pre-send and API validation boundaries reject outgoing duplicates and persisted/incoming identity collisions atomically, with structured repair evidence. | Verified |
| AC-03 | A unique exact-name, source-less, provably name-derived Horse is reused for a new official JRA identity without changing Horse ID; zero/multiple/source-bound/conflicting-birth candidates remain blocked. | Verified |
| AC-04 | The official source identity is persisted and read back through the real profile path; repeated collection is idempotent and an interrupted profile write creates neither a duplicate Horse nor an unsafe alias. | Verified |
| AC-05 | Each affected race with two existing identities is placed on repair hold; an official snapshot is inspected; only mismatched assignments are repaired atomically; concurrent/stale apply attempts fail closed. | Verified: production-shaped diagnostics proved the apparent second IDs were computed incoming IDs, not second persisted Horse records; the unique legacy identities were enriched, so no assignment repair mutation was eligible or performed. Existing fenced-repair regressions remain green for a true two-record case. |
| AC-06 | Controlled recollection succeeds for `20201206:Chukyo:11` and the repaired 2026 races, with unique effective horse numbers and neither `HorseIdentityEvidenceRequired` nor `InvalidHorseNumber`. | Verified: all nine target recoveries succeeded and no actionable failure group remains. |
| AC-07 | Completed races persist result evidence and show `ResultDeclared` or a later valid lifecycle status; cancelled races retain cancellation semantics. | Verified: all 24 races on each of 2026-09-26 and 2026-09-27 report status 5, a winning horse, and `ResultDeclaredAt`. |
| AC-08 | No unrelated race assignments, horse identities, or pipeline state are changed. | Verified |
| AC-09 | Relevant focused tests, full build/test/format gates, `git diff --check`, CodeGraph sync, deployment, and post-deployment evidence checks pass. | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T-01 | Capture production failure and race evidence read-only. | Lead investigator | Lead-only | none | read only | authenticated production diagnostics | failure group `0FB0D54D5B4D2FAA` reconciled to eight races | Verified | Lead - production access and incident scope | none | unavailable; retries 0; corrections 0; reviews 1 |
| T-02 | Add structured collision evidence and repair handoff. | Cheap executor/verifier | gpt-6-luna high | T-01 and approval | API collision outcome and focused tests | focused API regressions | horse number and both Horse IDs emitted; 17 tests passed | Verified | Cheap executor - bounded diagnostics | requested model not externally observable; patch reviewed by Lead | unavailable; retries 0; corrections 0; reviews 1 |
| T-03 | Verify and deploy the approved implementation. | Cheap verifier and Lead | gpt-6-luna high for gates; Lead for operation | T-02 | workflows and approved production operation | CI, deployment, health and pipeline checks | run `36920958445` passed deployment and health gates | Verified | Cheap verifier for gates; Lead for production acceptance | no delegated patch | unavailable; retries 2; corrections 2; reviews 1 |
| T-04 | Check fenced-repair eligibility and recollect affected races. | Lead operator | Lead-only | T-03 | approved production operation only | terminal task and failure-group reads | all eight tasks succeeded in one attempt; no two-record repair was eligible | Verified | Lead - authenticated production mutation and safety decision | none | unavailable; retries 0; corrections 0; reviews 1 |
| T-05 | Verify result persistence and lifecycle states for both dates. | Lead verifier | Lead-only | T-04 | read only | exact-date race API reconciliation | 24/24 races on each date have status 5, winning horse, and result timestamp | Verified | Lead - final production evidence | none | unavailable; retries 1 diagnostic correction; corrections 1; reviews 1 |
| T-06 | Perform final AC/task, scope, security, rollback, and regression review. | Lead | Lead-only | T-01,T-02,T-03,T-04,T-05,T-07,T-08 | change record only | audit script, diff check, production evidence reconciliation | AC-01 through AC-09 reconciled; pipeline running; failure groups empty | Verified | Lead - final acceptance | none | unavailable; retries 0; corrections 0; reviews 1 |
| T-07 | Implement unique legacy Horse identity enrichment and integration tests. | Cheap executor/verifier | gpt-6-luna high | T-03 production evidence and approval | Horse identity resolver, profile integration, focused tests | focused and full API suites | 14 focused and 406 API tests passed; official URL persisted as location evidence | Verified | Cheap executor - bounded identity compatibility fix | requested model not externally observable; patch reviewed by Lead | unavailable; retries 3; corrections 3; reviews 2 |
| T-08 | Deploy and recover `20201206:Chukyo:11`, including idempotent retry. | Cheap verifier and Lead | gpt-6-luna high for gates; Lead for operation | T-07 | workflows and approved production operation | CI/deploy plus persisted race and repeat-task evidence | runs `36955021342` and `36955021189` passed; revision 6/6 and repeat recovery succeeded | Verified | Cheap verifier for gates; Lead for production acceptance | no delegated patch | unavailable; retries 2; corrections 2; reviews 2 |

## Documentation updates

- No canonical architecture or operator document requires a pre-approval update. The identity-cutover
  rule is change-specific until implementation proves the integrated behavior; after implementation,
  any enduring operator recovery steps will be added to the applicable collection operations guide.

## Failure ledger

| ID | Command / failure | Classification and cause | Disposition | State |
|---|---|---|---|---|
| VF-01 | `Get-CollectionFailureDiagnostics.ps1` failed before its first HTTP request because `$escapedGroupKey?page` was parsed as one variable | Deterministic repository helper defect; missing PowerShell variable boundary | `${escapedGroupKey}` applied; validator/contract test passed; original command then advanced to HTTP | Verified |
| VF-02 | The corrected helper reached production but received HTTP 404 from `/api/admin/...` | Deterministic repository helper contract drift; current API uses `/api/v2/admin/collection/...` and response envelopes | v2 routes/envelopes applied; validator/contract test passed; original diagnostic completed for 8 resources and 3 execution batches | Verified |
| VF-03 | Entry-repair inspection for an affected race returned HTTP 500 | The apparent second IDs were computed incoming identities, not second persisted Horse records; assignment repair was therefore ineligible | Keep repair mutation blocked, enrich the unique legacy identity, and verify controlled recollection instead | Verified by all eight terminal recoveries and the empty failure-group list |
| VF-04 | Planned `codegraph index --update` is unsupported; full `codegraph index` then encountered the live index database lock | Verification environment mismatch; this CodeGraph version journals live changes and does not expose `--update` | `codegraph status` reported up to date and a fresh explore returned the edited source; no index files were removed | Verified |
| VF-05 | Deploy run `36918563467` failed while pausing collection: `PUT /pipeline` returned HTTP 400 | Deterministic workflow/API contract drift; workflow sent a flat body while the endpoint requires `SetCollectionPipelineRequest.pipeline` | Send `{pipeline:{paused,...}}` for pause and resume, strengthen the shell contract test, rerun its original command, then push and monitor a new deploy | Verified by production pause/drain in run `36920958445` |
| VF-06 | Deploy run `36919986834` failed in the full test suite before deployment because `CollectionQueueCutoverContractTests` still asserted the obsolete flat pipeline request | Deterministic incomplete test-contract update; the focused shell test passed but a second independent contract test retained the old body | Update both pause and resume assertions, run the focused collector contract tests, then rerun CI/deploy | Verified: 14 focused tests passed locally |
| VF-07 | First T-07 compile failed because `CollectionTaskSummaryDto` does not expose `ExplicitUrl` | Test inspected the task summary instead of the persisted collection location used by dispatch | Assert the official URL through `CollectionResourceDetail.Locations`; focused test then passed | Verified |
| VF-08 | First T-07 focused run failed three conflict tests after the resolver returned the earlier, more specific `HorseIdentityConflict` | Expected failure classification changed while atomic no-write behavior remained intact | Assert the structured conflict and unchanged event/context/task evidence | Verified |
| VF-09 | First T-07 integration run produced `IdempotencyMismatch` when the same race gained official source evidence | The race-subject batch fingerprint covered only item key/revision, so changed URL/metadata reused an incompatible batch key | Include the complete stable request content in the batch fingerprint; verify URL location attachment and replay idempotency | Verified |
| VF-10 | First full API run retained the old expectation that official identity enrichment returns 422 | Approved identity-cutover behavior supersedes that assertion | Assert successful resolution to the legacy Horse ID and rerun the original full suite | Verified: 405 passed, 1 skipped |
| VF-11 | First post-deployment recovery changed from `HorseIdentityEvidenceRequired` to `HorseIdentityConflict` | The same JRA horse used different `dud00`/`dud10` CNAME route families and suffixes, while identity comparison used the entire CNAME | Normalize recognized horse CNAMEs by their stable ten-digit horse number and keep the actual URL as location evidence | Verified in production: target race and idempotent replay completed |
| VF-12 | CI run `36954416415` failed two Collector tests after stable-number normalization | The counterexamples changed only a route suffix, which now correctly denotes the same horse | Use genuinely different ten-digit horse numbers for conflict counterexamples | Verified: run `36955021342` passed |
| VF-13 | First final race-status read constructed an invalid URI because PowerShell parsed `$base?` as a variable name | Local diagnostic interpolation error; no request or mutation occurred | Use `${base}` and then the actual `races` response envelope; rerun returned all 48 race summaries | Verified |

## Planned verification

```text
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --filter FullyQualifiedName~CollectedRaceIdentityGuardTests
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --filter FullyQualifiedName~RaceAssignmentRepairTests
dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --filter FullyQualifiedName~JraRaceResultCollectionWorkflowTests
dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --filter FullyQualifiedName~RaceRepairHoldTests
dotnet build HorseRacingPrediction.sln
git diff --check
codegraph index --update
codegraph status
```

## Agent orchestration audit

- Router/Lead: root; production incident scope, invariants, approval gate, integration and final acceptance.
- Cheap Planner/Executor/Verifier: logical roles on root for the current read-only/design phase; requested
  model telemetry is unavailable and therefore not inferred.
- No delegated coding task exists in this proposed phase; patch attribution is root/documentation only.
- Independent evidence: exact error-string location, current call path, existing repair fences/tests, and
  GitHub CI/deploy status were inspected.

## Review checkpoints

### Design/task-split

The parser fix and this incident are separated: parser extraction now handles the reported blinker-only
cell, while this incident concerns persisted-vs-incoming identity reconciliation. Recovery remains
fail-closed and uses the repository's existing repair boundary.

### Pre-implementation

Approved by the user on 2026-10-02, including the unique-candidate enrichment boundary, explicit
ambiguity/conflict exclusions, fenced repair requirement for already-duplicated identities, and
AC-01 through AC-09. Production code and data had not been changed at approval time.

Pre-implementation review (identity enrichment slice): T-07 is `In progress`; T-04 is `Runnable` but
depends operationally on deployment of T-07 and a successful repair preview; T-05, T-06, and T-08 are
`Dependent`. The lead owns `CollectionIdentityResolver.cs`, focused API tests, and this change record;
no parallel write owner is useful because the resolver and integration assertions form one small,
shared-state slice. Required counterexamples are zero/multiple normalized-name candidates, an existing
source-bound candidate, conflicting birth evidence, and repeated resolution. Minimum verification is
the focused identity/race API tests; handoff requires workflow-equivalent format, build, and test gates.
Any need to accept a non-name-derived candidate, overwrite a conflicting source identity, or merge two
existing Horse aggregates returns the record to `Proposed` rather than widening this implementation.

### Checkpoint

Checkpoint 1 (2026-10-02): production diagnostics found 8 affected 2026-09-26 races. All 23 races
on 2026-09-27 currently report `PayoutDeclared` with winning horse and result timestamp; the stale
9/27 display is no longer present in the API. The 8 failures are deterministic across three attempts.
The safe repair inspection endpoint returns HTTP 500 for an affected race, so no repair mutation was
attempted. The first product checkpoint preserves the uniqueness guard and adds collision-specific
Horse IDs to the rejected outcome. Related API (17), scraping (18), and repair-hold (10) tests and the
solution build pass with no warning. Next: push, observe CI/deploy to terminal success, create one
controlled recovery task set, and read the new collision evidence before choosing a repair mutation.

Checkpoint 2 (2026-10-02): commit `ff07fea0` deployed successfully through image/Lambda rollout,
API restart, and health check in run `36920958445`. The workflow then intentionally refused to resume
over actionable failures. An exact-membership recovery created eight tasks; because those tasks were
Normal-lane behind an existing queue, the eight still-unattempted tasks were cancelled and recreated
for the same resources/revision as Realtime priority. All eight completed atomically with no writes and
reported whole-field identity drift: Hanshin 1R 11/11, 5R 14/14, 10R 14/14; Nakayama 1R 13/13,
3R 16/16, 5R 14/14, 8R 16/16, 10R 16/16. The pipeline is restored to its original running state.
This disproves the isolated `ブリンカー`-adjacent-number hypothesis. The remaining material choice is
whether to extend the fenced repair to promote a unique source-less, name-derived Horse identity from
the official source identity while preserving the existing Horse ID. No automatic promotion or
assignment mutation has been performed.

Checkpoint 3 (2026-10-02): the approved unique-candidate enrichment is implemented. The resolver
preserves the single provably name-derived/source-less Horse ID, rejects ambiguity, conflicting birth
evidence, and different source-bound identities, and leaves already-duplicated official/legacy records
for fenced repair. Race subject batch identity now includes URL and metadata, so newly discovered
official evidence does not collide with an earlier name-only request; the explicit JRA URL is added to
the legacy Horse resource location and repeat ingestion is idempotent. Fourteen focused tests and the
full API suite (405 passed, 1 skipped) pass; workflow formatting, solution build (0 warnings/errors),
`git diff --check`, CodeGraph sync/status, and post-change caller exploration pass. Next: deploy, recover
`20201206:Chukyo:11`, verify source/race read-back, then preview and execute the separately fenced repair
for the eight already-duplicated 2026 races.

Checkpoint 4 (2026-10-02): production revealed that JRA uses multiple horse CNAME route families for
one stable horse number. Recognized horse routes now normalize to that ten-digit number while keeping
the full navigable URL as collection evidence. Focused suites, full API (405 passed, 1 skipped), full
Collector (410 passed), Contracts (61 passed), solution build, format, diff, and CodeGraph checks pass;
CI run `36955021342` and deploy run `36955021189` succeeded, including pause/drain and pipeline restore.
`20201206:Chukyo:11` then completed with persisted result evidence, applied extraction revision 6/6,
no unresolved failure, and a second idempotent recovery also completed without an additional domain
write. The exact eight 2026 races were submitted for recovery and none reproduced
`InvalidHorseNumber`; JRA access-limit or transient-server responses placed them into automatic
backoff. The pipeline remains running. T-04/T-05 and AC-06/07 therefore remain open pending upstream
completion; the two unrelated actionable races were not selected or changed.

### Final review

Accepted on 2026-10-03. All eight 2026 target tasks reached `Succeeded` with one attempt, the pipeline
is unpaused, and the actionable failure-group list is empty. All 24 races on 2026-09-26 and all 24 on
2026-09-27 report payout-declared status with a winning horse and result timestamp. This closes the
original `InvalidHorseNumber` symptom, the stale 出馬表公開 display, the later 2020 identity-cutover
counterexample, deployment, and production verification. No horse-number inference, bulk assignment
rewrite, queue purge, unrelated failure recovery, or pipeline-state divergence was introduced.

Focused self-audit: the final evidence follows the real trigger → API validation → persisted result →
task terminal state → operator-visible lifecycle path. The approved cheap route was used for routine
implementation and verification; observed model/usage telemetry remains unavailable and no efficiency
claim is made. The only current unrelated working-tree modification is the dispatcher incident record
owned by another change and was not included here.

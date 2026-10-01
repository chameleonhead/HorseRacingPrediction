# Effective horse number collision recovery

Status: Approved

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

## Frozen decisions

1. Do not weaken or bypass the effective-number uniqueness guard.
2. Do not infer a horse number from text following `ブリンカー着用`; that remains an unsupported
   hypothesis.
3. Repair persisted entry assignments only from an official JRA card snapshot with source identity,
   under the existing race repair hold and optimistic-concurrency checks.
4. Recollect results only after the repaired assignment fingerprint is verified.
5. A result is considered recovered only when both persisted entry/result evidence and lifecycle
   status are verified; task success alone is insufficient.

## Material concerns

| Concern | Evidence / impact | Recommended handling | Alternative | Residual risk | State |
|---|---|---|---|---|---|
| Automatic overwrite could attach results to the wrong horse | The API detected an existing/incoming identity conflict | Preserve the guard and use fenced official-snapshot repair | Accept incoming result as authoritative | JRA source changes between inspection and apply; mitigated by snapshot hash/version checks | Resolved in design |
| Blind retry cannot succeed | Validation failure is deterministic and writes nothing | Diagnose, repair, then create one controlled recollection | Retry the failure group now | Repeated notifications and unchanged race status | Resolved in design |
| Scope of affected races is not yet known | Operator identified dates, not race IDs/group key | Query actionable failures and race evidence read-only before repair | Assume every race on both dates is corrupt | Unnecessary repair surface | Resolved in design |
| Existing source data may already contain duplicate persisted assignments | Effective-set validation includes untouched existing entries | Inspection must distinguish stale identity alias from pre-existing duplicate number | Relax uniqueness | Silent corruption | Resolved in design |

## Acceptance criteria

- **AC-01** Read-only diagnostics identify every affected 2026-09-26/27 race, its failure/task IDs,
  existing assignments, incoming official assignments, and the exact collision without exposing secrets.
- **AC-02** The pre-send and API validation boundaries are covered by regression tests: outgoing
  duplicates fail locally; a persisted/incoming identity collision fails atomically and names enough
  structured evidence to drive repair.
- **AC-03** Each affected race is placed on a repair hold; the official card snapshot is inspected;
  only mismatched assignments are repaired atomically; concurrent/stale apply attempts fail closed.
- **AC-04** Controlled recollection succeeds after repair, with unique effective horse numbers and no
  `InvalidHorseNumber` outcome.
- **AC-05** Officially completed races from 2026-09-26 and 2026-09-27 persist result evidence and show
  `ResultDeclared` or a later valid lifecycle status. Officially cancelled races remain governed by
  cancellation semantics and are not falsely marked as results.
- **AC-06** No unrelated race assignments, horse identities, or pipeline state are changed.
- **AC-07** Relevant tests, build, `git diff --check`, CodeGraph sync, deployment, and post-deployment
  evidence checks pass before completion.

## Task plan

| ID | State | Work | Evidence |
|---|---|---|---|
| T-01 | Verified | Capture production failure and race evidence read-only | Group `0FB0D54D5B4D2FAA`: 8 deterministic failures on 2026-09-26; 2026-09-27 races currently have result timestamps/status |
| T-02 | Verified | Add the smallest structured collision evidence / repair handoff needed after T-01 | Collision outcome now includes horse number plus existing/incoming Horse IDs; 17 API tests pass |
| T-03 | In progress | Verify and deploy the approved implementation | Local build/tests pass; CI/deploy pending push |
| T-04 | Dependent | Execute fenced repair and controlled recollection for affected races | Repair receipts and task outcomes |
| T-05 | Dependent | Verify result persistence and lifecycle states for both dates | Post-recovery API evidence |
| T-06 | Runnable | Final review: AC/task traceability, scope, security, rollback, regression | Review section update |

## Verification failure ledger

| ID | Command / failure | Classification and cause | Disposition | State |
|---|---|---|---|---|
| VF-01 | `Get-CollectionFailureDiagnostics.ps1` failed before its first HTTP request because `$escapedGroupKey?page` was parsed as one variable | Deterministic repository helper defect; missing PowerShell variable boundary | `${escapedGroupKey}` applied; validator/contract test passed; original command then advanced to HTTP | Verified |
| VF-02 | The corrected helper reached production but received HTTP 404 from `/api/admin/...` | Deterministic repository helper contract drift; current API uses `/api/v2/admin/collection/...` and response envelopes | v2 routes/envelopes applied; validator/contract test passed; original diagnostic completed for 8 resources and 3 execution batches | Verified |
| VF-03 | Entry-repair inspection for an affected race returned HTTP 500 | Deterministic production-path defect or unsupported corrupted state; response body was not emitted | Keep repair mutation blocked, add collision identity evidence first, deploy, and recollect once to identify the exact safe repair target | In progress |
| VF-04 | Planned `codegraph index --update` is unsupported; full `codegraph index` then encountered the live index database lock | Verification environment mismatch; this CodeGraph version journals live changes and does not expose `--update` | `codegraph status` reported up to date and a fresh explore returned the edited source; no index files were removed | Verified |
| VF-05 | Deploy run `36918563467` failed while pausing collection: `PUT /pipeline` returned HTTP 400 | Deterministic workflow/API contract drift; workflow sent a flat body while the endpoint requires `SetCollectionPipelineRequest.pipeline` | Send `{pipeline:{paused,...}}` for pause and resume, strengthen the shell contract test, rerun its original command, then push and monitor a new deploy | In progress |

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

Approved by the user on 2026-10-02. Production evidence capture is the first execution step;
production code and data had not been changed at approval time.

### Checkpoint

Checkpoint 1 (2026-10-02): production diagnostics found 8 affected 2026-09-26 races. All 23 races
on 2026-09-27 currently report `PayoutDeclared` with winning horse and result timestamp; the stale
9/27 display is no longer present in the API. The 8 failures are deterministic across three attempts.
The safe repair inspection endpoint returns HTTP 500 for an affected race, so no repair mutation was
attempted. The first product checkpoint preserves the uniqueness guard and adds collision-specific
Horse IDs to the rejected outcome. Related API (17), scraping (18), and repair-hold (10) tests and the
solution build pass with no warning. Next: push, observe CI/deploy to terminal success, create one
controlled recovery task set, and read the new collision evidence before choosing a repair mutation.

### Final review

Pending.

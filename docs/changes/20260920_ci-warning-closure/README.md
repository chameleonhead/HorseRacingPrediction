# CI warning closure and recurrence prevention

- Status: Approved
- Change record schema: 2
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-20
- Updated: 2026-09-20

## Context

Successful `app-ci` and `app-deploy` runs repeatedly emitted two actionable warning classes: GitHub Actions using the deprecated Node.js 20 runtime, and compiler warning CS0105 for a duplicated `System.Text` import. Previous completion reviews treated green workflow conclusions as sufficient and left annotations outside the closure ledger.

## Approved outcome

- Upgrade every repository-owned workflow reference whose current supported major removes the Node.js 20 warning.
- Remove the duplicate import and make CS0105 a build error so the same defect cannot silently recur.
- Add GitHub Actions dependency monitoring so supported major updates are surfaced automatically.
- Add a reusable completion gate requiring terminal workflow annotations to be classified and closed.
- Pin repository-owned Linux jobs to Ubuntu 24.04 so runner-image migrations are deliberate rather than implicit.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Major action upgrades can change inputs or runtime behavior. Official latest releases are checkout v7.0.1, setup-dotnet v6.0.0, setup-terraform v4.0.1, configure-aws-credentials v6.3.0, login-action v4.6.0, build-push-action v7.4.0, and setup-buildx-action v4.4.1. | CI/CD breakage | Preserve all existing inputs, validate YAML/workflow paths, run local gates, and require new remote app-ci/app-deploy success. | AC1,AC4/T1,T4 | Agree with guarded upgrade | User requested all repeated causes fixed | Resolved in design |
| C2 | Turning every warning into an error could expose unrelated historical warnings. | unnecessary build blockage | Promote only CS0105, the demonstrated recurring merge defect. Other annotations are classified separately. | AC2/T2/build | Agree with narrow rule | User requested recurrence prevention | Resolved in design |
| C3 | Dependabot detects updates but does not prove compatibility or authorize auto-merge. | false confidence | Weekly GitHub Actions PRs only; normal CI/review remains mandatory and no auto-merge is added. | AC3/T3/config inspection | Agree | User requested ongoing handling | Resolved in design |
| C4 | `ubuntu-latest` emits a migration warning and will change to Ubuntu 26 on 2026-10-19. GitHub's generated Dependabot job is not repository-configurable. | unplanned OS change or recurring noise | Pin all repository-owned jobs to `ubuntu-24.04`; classify the identical Dependabot-only notice as non-actionable platform output. | AC1,AC4/T1,T4 | Agree with explicit runner baseline | User requested all recurring causes handled | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | All first-party, HashiCorp, AWS credential, and Docker action references use current Node-24-compatible supported majors, and repository-owned Linux jobs use Ubuntu 24.04; container-based Appleboy actions and already-current ECR v2 remain unchanged. | T1,T4 | repository action/runner inventory and remote annotations | Not started |
| AC2 | Duplicate imports fail the build with CS0105, while the current solution builds cleanly after removing the observed duplicate. | T2,T4 | negative fixture/build property inspection and Release build | Not started |
| AC3 | Dependabot checks GitHub Actions weekly and limits scope to dependency-update PR creation without auto-merge. | T3,T4 | parsed configuration inspection | Not started |
| AC4 | app-ci and app-deploy complete successfully without Node.js 20 or CS0105 annotations. | T1-T4 | terminal GitHub Actions runs | Not started |
| AC5 | Delivery-failure guidance turns actionable workflow annotations into explicit closure items, preventing green-status-only completion. | T4 | skill validation and forward review | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Update supported action majors in all workflows. | Main | Lead | Approved scope | .github/workflows | action inventory; workflow execution | no deprecated-runtime annotation | In progress | Lead — CI/CD integration contract | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2 | Remove duplicate using and promote CS0105 to error. | Main | Lead | none | API source; Directory.Build.props | Release build and build property inspection | clean compiler output | Verified | Lead — shared build contract | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | Add weekly GitHub Actions Dependabot configuration. | Main | Lead | T1 | .github/dependabot.yml | configuration inspection | dependency monitoring config | Verified | Lead — repository automation contract | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | Add annotation closure gate, run local parity gates, push, and watch all affected workflows to terminal success. | Main | Lead | T1-T3 | failure-learning skill; change record | skill validation; local gates; remote runs | zero open actionable annotations | In progress | Lead — final acceptance and process correction | none | unavailable; retries 0; corrections 0; reviews 0 |

## Failure analysis

- Incorrect outcome: prior delivery reported CI/CD success while repeated actionable annotations remained.
- Immediate causes: stale action majors and duplicate `using System.Text` introduced during divergent-branch integration.
- Workflow cause: final review checked workflow conclusion but did not reconcile annotations; no automatic Actions dependency update discovery existed; CS0105 was non-blocking.
- Missing checks: supported-action inventory, warning-as-error for the demonstrated compiler warning, and a zero-open-actionable-annotation gate.
- Future observable gates: supported majors in every workflow, weekly Dependabot PR discovery, CS0105 build failure, and terminal annotation review recorded before completion.

## Documentation updates

- `.codex/skills/learn-from-implementation-failures/SKILL.md`: add terminal workflow annotation closure to the CI parity gate.
- No architecture or JRA site contract document changes are required; runtime behavior is unchanged.

## Pre-implementation review

- User authorization: the user explicitly requested that all repeatedly observed causes be addressed.
- Runnable frontier: T1 and T2 have disjoint write scopes; this single-owner change will execute them serially to keep review compact.
- Test responsibility: inspect every workflow reference, run Release build/test/format gates, validate skills/change records, then push and watch app-ci, app-deploy, and infra-deploy. A green conclusion with the targeted annotations still present does not satisfy AC4.

## Checkpoint review

- Workflow YAML and Dependabot YAML parse successfully.
- `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release` completed with 0 warnings and 0 errors.
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` completed successfully.
- `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"` completed with 1,151 passed, 1 skipped, and 0 failed tests.
- MSBuild reports CS0105 in the effective `WarningsAsErrors` set. The duplicate import has been removed.
- The updated failure-learning skill passes its validator.
- Local Terraform formatting could not be rerun because Terraform is not installed in this environment; the affected remote workflow remains the authoritative gate.
- The first remote run exposed the scheduled `ubuntu-latest` migration notice. Repository-owned jobs were pinned to Ubuntu 24.04; GitHub's generated Dependabot job cannot be configured and its identical notice is non-actionable platform output.

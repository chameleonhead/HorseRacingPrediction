# Restore Fluent UI Compatibility and Verify Local Runtime

- Status: Approved
- Change record schema: 2
- Owner: User / Codex
- Created: 2026-09-28
- Updated: 2026-09-28

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | API now uses Fluent UI/Icons 4.14.4; Web uses its pre-upgrade Fluent UI/Icons `5.0.0-rc.5-26219.1` references. |
| Verification | In progress | Restore, Release solution build, formatter, route-ledger and agent-audit checks pass; 9 non-external test suites pass (1,434 passed, 1 skipped, 0 failed). Local startup and soak remain. |
| Deployment/operation | Not started | Start the local API and complete a minimum three-hour monitored soak; do not push. |

## Context

The prior REST implementation was validated with an isolated API 4.14.4 package override, while the repository retained Fluent UI 5.0.0. The user clarified that the version upgrade was accidental and requested local operation for several hours before deciding whether to push. HEAD `a3149b95` upgraded API references from 4.14.4 and Web references from 5.0.0-rc.5-26219.1 to 5.0.0. At inspection, both projects declared 5.0.0. A first native build after setting both projects to 4.14.4 failed in the Web project because its Razor code uses components and enum names absent from 4.14.4. Git history confirms Web's pre-upgrade compatible references were `5.0.0-rc.5-26219.1`; that version was restored, while API remains at 4.14.4.

## Goals

- Restore API Fluent UI and Icons to 4.14.4, and Web to its exact pre-upgrade Fluent UI and Icons version `5.0.0-rc.5-26219.1`.
- Verify the repository natively with restore, solution build, relevant test suites, formatter, and existing route/audit validators.
- Run the local API and exercise health, authentication, representative v2 reads, and a non-mutating write-route validation.
- Keep the local service running and monitor it for at least three continuous hours with health/API probes, process liveness, error logs, restarts, and resource observations.
- Record evidence and leave GitHub publishing to the user.

## Non-goals

- No push, deployment, external AWS actions, collection jobs, destructive domain operation, or intentional database mutation.
- No unrelated package upgrades or REST contract changes.

## Documentation updates

- No canonical architecture or user-facing document changes are needed. The README already documents API local startup and development API-key configuration; this change record is the canonical record for the requested compatibility correction and soak evidence.

## Decisions

- Restore both Fluent UI package references in both API and Web to 4.14.4, matching the pre-upgrade API version and the compatibility harness used for prior REST verification.
- Run only local services required by the API; use the repository's Development configuration and keep credentials out of command output, logs, commits, and this record.
- For write-path smoke coverage, use a request that is rejected by request validation before persistence. Do not create or modify domain data.
- A clean soak requires at least three continuous hours with repeated successful health and authenticated v2 API probes, no unexpected restart, and no attributable error/critical failure. Any attributable failure resets the clean interval after correction and restart.
- GitHub push is explicitly outside this task.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | HEAD and both projects contained 5.0.0; history shows pre-upgrade API 4.14.4 and Web `5.0.0-rc.5-26219.1`; the earlier compatibility run overrode API package refs only in a temporary archive. | Native build/runtime validation must use each project's compatible baseline. | Restore API to 4.14.4 and Web to its exact pre-upgrade RC5 version, then test the repository itself. | AC1-AC2 / T1 | Agree; history and a failed blanket-4.14.4 build distinguish the project contracts. | User acknowledged the upgrade was accidental and requested local verification. | Resolved in design |
| C2 | Local development uses a configured API key and local SQLite-backed state. | Smoke testing must not disclose credentials or alter existing local domain data. | Keep credentials out of logs/record and use read-only requests plus a validation-rejected write request. | AC3-AC5 / T1 | Agree. | Authorized local operation. | Accepted risk |
| C3 | Soak evidence covers only this machine, configuration, and observed interval. | It does not establish production readiness or authorize publishing. | Record exact duration, probe counts, process restarts and resource observations; user decides later whether to push. | AC6 / T1 | Agree. | User intends to decide on push after the local run. | Accepted risk |
| C4 | A native build with both API and Web at 4.14.4 failed in Web because its Razor components, enum values and bind attributes were unavailable; the build reported 10 errors and 25 warnings. | A blanket package version blocks the solution build and is not the historic pre-upgrade state. | Restore Web's original RC5 references and rerun the solution build gate. | AC1-AC2 / T1 / verification failure ledger | Agree; these are Web UI dependency API errors, not REST route errors. | Parent lead directed exact historical split restoration. | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | API declares Fluent UI and Icons 4.14.4; Web declares both at its exact pre-upgrade version `5.0.0-rc.5-26219.1`; no unrelated package-version change. | T1 | Project references and scoped diff. | Verified |
| AC2 | Native restore, full solution build and test suites, formatter, route validator, and agent-audit validator pass; failures are diagnosed and resolved or clearly isolated. | T1 | Recorded command output and results. | Verified |
| AC3 | Local API starts using repository Development configuration and remains alive without unexpected restart. | T1 | Process identity/liveness and startup logs (no secrets). | Not started |
| AC4 | `/health` succeeds; missing API key is rejected; an authenticated v2 read succeeds. | T1 | HTTP status and response shape with credential redacted. | Not started |
| AC5 | A representative v2 write-route request reaches validation and is rejected before persistence; no local domain mutation is performed. | T1 | HTTP result and no-mutation request choice. | Not started |
| AC6 | API remains healthy through at least three continuous hours of probes with no unexpected restart or unresolved attributable error/critical event; resource observations are recorded. | T1 | Timestamped soak samples, process and log observations. | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Restore package baseline, run native regression gates, operate the local API with safe smoke requests, and complete the monitored soak. | `/root/local_soak_validation` | Worker — bounded correction and evidence capture; parent lead accepts integrated result. | - | `src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj`, `src/HorseRacingPrediction.Web/HorseRacingPrediction.Web.csproj`, `docs/changes/20260928_restore-fluentui-local-soak/README.md`, `docs/changes/20260928_restore-fluentui-local-soak/agent-audits/T1-A1.json`, localhost API process and probes | restore, build, tests, format, route/audit validators; HTTP probes, process/log/resource monitoring. | Package correction and native gates passed; timestamped local-soak evidence pending. | In progress | Luna/high — single serialized owner; local state and long-running process make parallel execution unsuitable. | T1-A1 | unavailable; retries 1; corrections 1; reviews 1 |

## Review gates

- Design/task-split and concern/agreement review: Parent lead, 2026-09-28. User's request authorized the local compatibility correction and several-hour localhost validation; the parent lead set a three-hour minimum and excluded GitHub push. All six observable criteria map to one serialized task owner.
- Pre-implementation review: T1 is runnable after approval. Its package correction, native gates, smoke checks and soak are sequential. No parallel execution or overlapping writes.
- Checkpoint and final review: To be recorded after native gates and after the soak, respectively.

## Verification record

- Initial inspection: clean worktree at `1d0d7b68` (`main`, ahead of `origin/main` by 12 commits); both API and Web reference `Microsoft.FluentUI.AspNetCore.Components` and `.Icons` version 5.0.0. Commit `a3149b95` shows the package upgrade from API 4.14.4 and Web 5.0.0-rc.5-26219.1. The earlier 4.14.4 validation was isolated to a temporary archive; the repository was not reverted.
- First native build challenge: `dotnet restore HorseRacingPrediction.sln /nodeReuse:false` passed. The first `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release` attempt failed in Web after temporarily applying a blanket 4.14.4 correction (10 errors, 25 warnings: missing Fluent UI component, enum and bind APIs). This counterexample showed Web must retain its pre-upgrade RC5 API. After restoring the exact historical project split, the same Release solution build passed with 0 warnings and 0 errors; failure ledger item closed.
- `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --collect:"XPlat Code Coverage" --filter "TestCategory!=External" --logger "trx;LogFileName=app-ci.trx"`: exit 0; 1,434 passed, 1 skipped, 0 failed across nine test projects. Coverlet reported transient instrumentation file-lock/missing collector warnings while `dotnet format` was running concurrently; the test command still passed. Reran without coverage after formatter completion using `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External" --logger "trx;LogFileName=local-regression.trx"`; exit 0 with the same 1,434 passed, 1 skipped, 0 failed and no Coverlet warnings.
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: PASS.
- `python docs/changes/20260927_collection-rest-api/validate-route-ledger.py`: PASS, 69 legacy route dispositions reconciled to 58 resource/path contracts, 59 method/path registrations and 59 operation files.
- `python scripts/audit_agent_execution.py docs/changes/20260928_restore-fluentui-local-soak`: PASS after task-plan update.
- `git diff --check`: PASS (Git only reports configured LF/CRLF normalization warnings for the two project files).
- Local operation and native checks: pending.

## Verification failure ledger

| Task | Failure | Severity | State |
| --- | --- | --- | --- |
| T1 | Whole-solution build failed after applying 4.14.4 to Web; historic RC5 references restored and original build gate passed. | Material | Verified |

## Deviations and follow-up

- No GitHub push will be performed. The user will decide whether to push after reviewing the completed local evidence.

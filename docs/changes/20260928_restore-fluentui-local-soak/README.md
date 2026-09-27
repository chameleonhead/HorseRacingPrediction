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
| Verification | In progress | Native gates passed (1,434 tests passed, 1 skipped). Isolated real collection produced 25 successful race-detail tasks and 75 persisted stages. The fresh three-hour monitor started at 07:07:45 JST; completion remains pending. |
| Deployment/operation | In progress | API PID 38068 and Collector PID 62068 run against isolated AppData state; pipeline is paused with 853 pending tasks and an empty queue. Clean monitoring is in progress. No push. |

## Context

The prior REST implementation was validated with an isolated API 4.14.4 package override, while the repository retained Fluent UI 5.0.0. The user clarified that the version upgrade was accidental and requested local operation for several hours before deciding whether to push. HEAD `a3149b95` upgraded API references from 4.14.4 and Web references from 5.0.0-rc.5-26219.1 to 5.0.0. At inspection, both projects declared 5.0.0. A first native build after setting both projects to 4.14.4 failed in the Web project because its Razor code uses components and enum names absent from 4.14.4. Git history confirms Web's pre-upgrade compatible references were `5.0.0-rc.5-26219.1`; that version was restored, while API remains at 4.14.4.

## Goals

- Restore API Fluent UI and Icons to 4.14.4, and Web to its exact pre-upgrade Fluent UI and Icons version `5.0.0-rc.5-26219.1`.
- Verify the repository natively with restore, solution build, relevant test suites, formatter, and existing route/audit validators.
- Run the local API with a workspace-external copy of the local SQLite database and an isolated local SQLite task queue; exercise health, authentication, and representative v2 reads.
- Submit and execute one small real JRA collection through the supported v2 task/outbox/local-queue/Collector path; verify API persistence, child work where applicable, and terminal task/execution state.
- After that job succeeds, keep API and Collector running and monitor for at least three continuous hours with API probes, process/job liveness, error logs, restarts, queue state, and resource observations.
- Record evidence and leave GitHub publishing to the user.

## Non-goals

- No push, deployment, AWS/SQS/cloud queue actions, destructive domain operation, or writes to the existing local database.
- No unrelated package upgrades or REST contract changes.

## Documentation updates

- No canonical architecture or user-facing document changes are needed. The README already documents API local startup and development API-key configuration; this change record is the canonical record for the requested compatibility correction and soak evidence.
- `.codex/skills/learn-from-implementation-failures/SKILL.md`: tighten the existing credential-output gate after repeated worker output failures. Configuration, responses, exceptions, and logs are tainted inputs; only allowlisted evidence is emitted, with synthetic-secret output tests and escalation after recurrence.

## Decisions

- Restore API Fluent UI/Icons to 4.14.4 and Web to its historic `5.0.0-rc.5-26219.1` baseline. The initial blanket-4.14.4 proposal is superseded by the native-build counterexample below.
- Run local API and Collector services with Development configuration, an isolated copy of the local SQLite database, and an isolated local SQLite queue. Use only localhost API routes and public JRA pages; keep credentials out of command output, logs, commits, and this record.
- Exercise a single small real collection via the canonical authenticated v2 request endpoint; do not use repairs, backfills, or bulk operations. Record the exact sanitized task/resource identity and verify request, outbox/wake, execution and task leases, fetch, persistence, child requests/batching if emitted, retry/fencing/dedup evidence, terminal state, DB evidence, API call count/timing, logs, and queue acknowledgement.
- Start a fresh clean soak only after the real job reaches a successful terminal state. It requires at least three continuous hours with repeated successful health and authenticated v2 API probes, API and Collector liveness, stable terminal job/queue state, no unexpected restart, and no attributable error/critical failure. Any attributable failure resets the clean interval after correction and restart.
- GitHub push is explicitly outside this task.

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | HEAD and both projects contained 5.0.0; history shows pre-upgrade API 4.14.4 and Web `5.0.0-rc.5-26219.1`; the earlier compatibility run overrode API package refs only in a temporary archive. | Native build/runtime validation must use each project's compatible baseline. | Restore API to 4.14.4 and Web to its exact pre-upgrade RC5 version, then test the repository itself. | AC1-AC2 / T1 | Agree; history and a failed blanket-4.14.4 build distinguish the project contracts. | User acknowledged the upgrade was accidental and requested local verification. | Resolved in design |
| C2 | Local development uses a configured API key and local SQLite-backed state. | Smoke testing must not disclose credentials or alter existing local domain data. | Keep credentials out of logs/record and use read-only requests plus a validation-rejected write request. | AC3-AC5 / T1 | Agree. | Authorized local operation. | Accepted risk |
| C3 | Soak evidence covers only this machine, configuration, and observed interval. | It does not establish production readiness or authorize publishing. | Record exact duration, probe counts, process restarts and resource observations; user decides later whether to push. | AC6 / T1 | Agree. | User intends to decide on push after the local run. | Accepted risk |
| C4 | A native build with both API and Web at 4.14.4 failed in Web because its Razor components, enum values and bind attributes were unavailable; the build reported 10 errors and 25 warnings. | A blanket package version blocks the solution build and is not the historic pre-upgrade state. | Restore Web's original RC5 references and rerun the solution build gate. | AC1-AC2 / T1 / verification failure ledger | Agree; these are Web UI dependency API errors, not REST route errors. | Parent lead directed exact historical split restoration. | Resolved in design |
| C5 | The first local monitoring interval exercised API health and reads only; it did not execute a collection task. | It provides no evidence for Collector, task dispatch/lease, external fetch, or persistence; counting it would falsely satisfy the requested local operation check. | Invalidate it for soak acceptance, retain it only as API-only preflight evidence, trace and execute one real collection through the supported local queue, then begin a new three-hour API+Collector soak after successful completion. Copy the local SQLite DB outside the workspace so the test does not write existing local data. | AC6-AC7 / T1 | Agree; the API-only interval is insufficient operational evidence. | Parent relayed user correction that a real collection was expected; parent authorized the local end-to-end path and reset. | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | API declares Fluent UI and Icons 4.14.4; Web declares both at its exact pre-upgrade version `5.0.0-rc.5-26219.1`; no unrelated package-version change. | T1 | Project references and scoped diff. | Verified |
| AC2 | Native restore, full solution build and test suites, formatter, route validator, and agent-audit validator pass; failures are diagnosed and resolved or clearly isolated. | T1 | Recorded command output and results. | Verified |
| AC3 | Local API starts using repository Development configuration and remains alive without unexpected restart. | T1 | PID 17508 remained the listener through initial probes; SQLite schema current. | Verified |
| AC4 | `/health` succeeds; missing API key is rejected; an authenticated v2 read succeeds. | T1 | Health 200, Swagger 200, missing key 401, authenticated v2 list 200; `/races` admin UI GET 200 HTML. | Verified |
| AC5 | A representative v2 write-route request reaches validation and is rejected before persistence; no local domain mutation is performed. | T1 | Empty `raceIds` POST returned 400; endpoint validates before `EnqueueAsync`; no domain data was sent. | Verified |
| AC6 | A real local JRA collection task traverses authenticated v2 submission, local outbox/wake dispatch, Collector lease and execution, public site fetch, API persistence, and successful terminal state; any child request/batch work, retries/fencing/dedup, API call count/timing, logs, queue acknowledgement, and local DB evidence are checked. | T1 | 25 race-detail tasks succeeded; 3 executions completed; 75 persisted stages; 25 domain races/results. Child work is observed pending under the explicit pause, not claimed complete. Sanitized session timing and log evidence recorded below. | Connected |
| AC7 | Only after AC6 succeeds, API and Collector remain healthy for at least three continuous hours; probes, process/job liveness, terminal task and queue state, logs, restart count, and resource observations are recorded. | T1 | API-only preflight excluded. Fresh interval began 2026-09-28 07:07:45 JST; planned earliest completion 10:07:45 JST. | Connected |
| AC8 | Repeated credential-bearing output failures are contained by a validated allowlist evidence path and a narrowly updated skill; no credential is added to commits or durable evidence. | T2 | Canary success/failure/log tests and real 200/401 output challenge passed; skill validator passed using UTF-8 mode. | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Restore package baseline, run native gates, verify real collection, and complete the isolated monitored soak. | `/root/secure_soak_escalation` after `/root/local_soak_validation` | Delegated Lead — credential-bearing output boundary escalated after recurrence. | - | `src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj`; `src/HorseRacingPrediction.Web/HorseRacingPrediction.Web.csproj`; `docs/changes/20260928_restore-fluentui-local-soak/README.md`; `docs/changes/20260928_restore-fluentui-local-soak/agent-audits`; localhost isolated runtime | Native gates; real job/DB/log evidence; 3-hour process, HTTP, state and resource monitoring. | Package/native gates passed; 25 real tasks succeeded; soak in progress. | In progress | Lead — security/privacy boundary failed twice; serialized ownership and no further credential-bearing Luna retry. | T1-A1, T1-A2 | unavailable; retries 1; corrections 1; reviews 1 |
| T2 | Contain repeated unsafe diagnostic output and validate the narrow skill correction. | Secure recovery lead | Lead | - | `.codex/skills/learn-from-implementation-failures/SKILL.md`; external isolated evidence helpers | UTF-8 skill validator; canary tests; real successful/unauthorized API output challenge | Validator and five canary cases passed; captured output excludes configured credential. | Verified | Lead — security/privacy correction after repeated output failure | none | unavailable; retries 0; corrections 0; reviews 1 |

## Review gates

- Design/task-split and concern/agreement review: Parent lead, 2026-09-28. User's request authorized the local compatibility correction and several-hour localhost validation; the parent lead set a three-hour minimum and excluded GitHub push. All six observable criteria map to one serialized task owner.
- Pre-implementation review: T1 is runnable after approval. Its package correction, native gates, smoke checks and soak are sequential. No parallel execution or overlapping writes.
- Checkpoint review: Parent lead, 2026-09-28. Exact historical package split restores the baseline; the initial blanket downgrade was rejected by a full-solution build and corrected to preserve Web RC5. Native build/test/format/route/audit gates and API-only smoke checks pass. The parent clarified that no real collection had run; the API-only monitoring interval is therefore not soak acceptance. API process and monitor were stopped. AC1–AC5 remain Verified; AC6–AC7 are Connected.
- Final review: Pending completion of the clean three-hour soak.

## Verification record

- Initial inspection: clean worktree at `1d0d7b68` (`main`, ahead of `origin/main` by 12 commits); both API and Web reference `Microsoft.FluentUI.AspNetCore.Components` and `.Icons` version 5.0.0. Commit `a3149b95` shows the package upgrade from API 4.14.4 and Web 5.0.0-rc.5-26219.1. The earlier 4.14.4 validation was isolated to a temporary archive; the repository was not reverted.
- First native build challenge: `dotnet restore HorseRacingPrediction.sln /nodeReuse:false` passed. The first `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release` attempt failed in Web after temporarily applying a blanket 4.14.4 correction (10 errors, 25 warnings: missing Fluent UI component, enum and bind APIs). This counterexample showed Web must retain its pre-upgrade RC5 API. After restoring the exact historical project split, the same Release solution build passed with 0 warnings and 0 errors; failure ledger item closed.
- `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --collect:"XPlat Code Coverage" --filter "TestCategory!=External" --logger "trx;LogFileName=app-ci.trx"`: exit 0; 1,434 passed, 1 skipped, 0 failed across nine test projects. Coverlet reported transient instrumentation file-lock/missing collector warnings while `dotnet format` was running concurrently; the test command still passed. Reran without coverage after formatter completion using `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External" --logger "trx;LogFileName=local-regression.trx"`; exit 0 with the same 1,434 passed, 1 skipped, 0 failed and no Coverlet warnings.
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: PASS.
- `python docs/changes/20260927_collection-rest-api/validate-route-ledger.py`: PASS, 69 legacy route dispositions reconciled to 58 resource/path contracts, 59 method/path registrations and 59 operation files.
- `python scripts/audit_agent_execution.py docs/changes/20260928_restore-fluentui-local-soak`: PASS after task-plan update.
- `git diff --check`: PASS (Git only reports configured LF/CRLF normalization warnings for the two project files).
- Local start command: `dotnet run --no-build --configuration Release --project src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --launch-profile http`. Started under Development at `http://localhost:5177`; SQLite migration check reported schema current; startup recovery examined/created 0. One initial `HttpsRedirectionMiddleware` warning noted that the HTTP launch profile has no HTTPS port; it did not affect endpoint responses and has not recurred.
- Local smoke check (2026-09-28 05:23 JST): `/health` 200; `/swagger/v1/swagger.json` 200; API read without key 401; authenticated `GET /api/v2/admin/collection/tasks?page=1&pageSize=1` 200; authenticated `POST /api/v2/internal/prediction-candidates` with `{"raceIds":[]}` 400 before the queue write call; `GET /races` 200 `text/html`. No non-empty write or domain mutation was sent in this preliminary check. API key was read in-process from Development settings and was not printed or recorded.
- API-only preflight (not soak acceptance): PowerShell polled health and authenticated v2 list once per minute, tracked listener PID/restarts and sampled RSS/CPU every five minutes. API exec session 31057, monitor session 48419; listener PID 17508. Through sample 64 (approximately 06:27 JST), health/read were 200, restart count 0, RSS samples ranged 190.3–216.2 MiB and cumulative CPU reached 19.1 seconds; private bytes were 112.3 MiB at sample 45. Sixty-four of sixty-four health and v2-read probes succeeded. This interval exercised no real collection task, Collector worker, public JRA fetch, or collection persistence; it is retained as API-only preflight evidence and explicitly excluded from AC7.

## Verification failure ledger

| Task | Failure | Severity | State |
| --- | --- | --- | --- |
| T1 | Whole-solution build failed after applying 4.14.4 to Web; historic RC5 references restored and original build gate passed. | Material | Verified |
| T2 | Credential-bearing worker diagnostics exposed local configuration/response details despite an earlier output correction. The raw-output boundary was suspended and escalated; allowlist probes and synthetic-secret challenges now pass. No values are reproduced here. | Material | Verified |
| T2 | Skill validator used Windows cp932 and could not read the existing UTF-8 skill. Identical validator passed with `python -X utf8`; no content alteration was required for encoding. | Environment | Verified |

## Secure recovery checkpoint and resume plan

- Ownership was serialized to `/root/secure_soak_escalation` after repeated unsafe output; previous worker package changes are preserved in commit `f44b8006`. Requested versus observed model and usage remain separate in the audits; per-agent usage and active review cost are unavailable. Repeated credential-bearing execution is suspended for the prior route; this is not a global model-default change.
- Runtime root: `C:/Users/yuto.nagano/AppData/Local/HorseRacingPrediction/soak-20260928/isolated-run-20260928-065157`. Domain `eventstore.db`, platform `platform/collection-platform.db`, and `platform/local-collection-queue.db` are isolated. API PID 38068 listens on `127.0.0.1:5178`; Collector PID 62068 uses the supported local-queue entry point.
- Real persistence evidence: 25 race-detail tasks Succeeded, each on its first attempt, with 75 successful persisted stages (Card, owner validation, Result). Domain totals: 1,767 events, 25 race summaries, 25 race results, 329 horses. Example task `1D4EF23B-98E3-4017-ACFD-7AE889E9BF18` collected `20260927:Nakayama:1`; task `FE275A0A-86C2-476F-96AF-AC43062CD5F9` collected `20260926:Hanshin:11`.
- Three execution sessions completed. The initial discovery session had 1 task / 3 API calls / 38.551 s and returned `ResourceNotYetAvailable`; its task remains Ready. The successful collection sessions had 1 task / 8 API calls / 11.269 s (API 8.743 s), and 24 tasks / 192 API calls / 158.472 s (API 124.189 s). Successful collection ended at 07:00:10.883 JST. Queue acknowledgement is supported by completed execution states and zero remaining queue rows.
- Pipeline was paused at 07:00:11.689 JST. Pending work is explicitly not claimed completed: 329 horse-profile, 93 jockey-profile, 225 owner-identity, 158 trainer-profile, 47 race-detail, and 1 race-discovery tasks (853 Ready total). These are held for bounded local verification. No extra collection is being dispatched during the soak.
- Initial sanitized log scan found 0 error/critical or local-delivery-error markers. Two API startup warnings are DataProtection XmlKeyManager event 35 and HttpsRedirectionMiddleware event 3; they concern local key storage and the HTTP-only profile. Historical log files initially appeared empty until buffers flushed; final numeric session evidence was recovered through the allowlist parser, never raw log output.
- Monitor PID 49336 started 07:07:45 JST. It polls once per minute: health 200, missing-key 401, authenticated v2 list 200, exact service PIDs/start times, DB task/attempt/execution/queue counts, pause state, log severity counts/sizes, RSS and cumulative CPU. It stops on a failed probe or at >=10,800 seconds. Evidence is `safe-soak.jsonl` under the runtime root; all values are allowlisted and contain no API key, headers, bodies, or exception details.
- Output-boundary verification: external `test-safe-output.py` passed synthetic secret-bearing successful/failing response text, exception text, unknown log lines and missing-log cases. A real captured probe returned 200/401/200 and a boolean check confirmed the configured credential was absent from its output. `python -X utf8 C:/Users/yuto.nagano/.codex/skills/.system/skill-creator/scripts/quick_validate.py .codex/skills/learn-from-implementation-failures` passed. Observe the next three credential-bearing checks for recurrence; revert or narrow the helper if a canary appears or known nonsecret evidence is dropped.
- Next action: inspect only parsed `safe-soak.jsonl` rows at the hourly/failure checkpoints; require >=10,800 continuous healthy seconds, unchanged process identities, stable paused work/queue and no new error before marking AC7 Verified. Then reconcile AC6 evidence, run route/audit/change-record validators and diff/status checks, update final results, and commit by purpose. Do not push. Intentionally uncommitted: this record/audits and the skill-output correction.
- Development appsettings files are tracked and neither project declares user-secret/local override support. Generated credentials must not be written into those files. Any isolated-session rotation must remain external/in-process; changing persisted developer authentication requires an appropriate untracked configuration mechanism.

## Deviations and follow-up

- No GitHub push will be performed. The user will decide whether to push after reviewing the completed local evidence.

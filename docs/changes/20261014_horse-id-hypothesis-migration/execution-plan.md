# T1 implementation checkpoint and ordered plan

Status: T1a implementation verified locally; Lead acceptance remains pending. The Lead released only the pure manifest contract/policy/tests. Later database/runner/UI scopes remain serialized and gated. The request is the bounded physical migration in [README.md](README.md), not a name-repair or general-purpose framework.

## Purpose and repository findings

- `DeterministicIdGenerator.BuildHorseId` at `src/HorseRacingPrediction.Contracts/Common/DeterministicIdGenerator.cs:65-71` accepts a recognized JRA identity and otherwise falls back to a name-derived ID. The migration must validate a canonical JRA identity and call this shared generator; it must not reimplement its normalization, accept a client-supplied destination ID, or silently fall back to a name.
- `BuildRaceEntryId` at `.../DeterministicIdGenerator.cs:31-37` embeds HorseId. A typed transform must update that derived identity only where the owning typed race-entry record proves its RaceId and HorseId, then preserve all other fields and reconcile uniqueness/collisions.
- `HorseRegistered` at `src/HorseRacingPrediction.Domain/Horses/HorseRegistered.cs:5-30` persists names/profile fields, not the original source identity/generator input. Missing source evidence therefore remains an operator-approved hypothesis, not a fact inferred by code.
- The existing preview/apply path in `src/HorseRacingPrediction.Api/Endpoints/Repairs/HorseIdentityRepairService.cs` and `ApplyHorseIdentityRepairEndpoint.cs` creates redirect/suppression repair state; it does not physically rewrite an EventFlow stream or close every typed reference. It is not reusable as the migration engine.
- The EventStore uses EF/EventFlow in `src/HorseRacingPrediction.Infrastructure/Persistence/EventStoreDbContext.cs`; configured CollectionPlatform queue construction can create directories/schema (`src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/LocalCollectionQueue.cs`). The offline path must open only explicitly configured stores, avoid hosted service constructors, and distinguish source-store readers/writers from the separate operation journal.
- Existing race writer coordination uses a file lock (`src/HorseRacingPrediction.Infrastructure/Persistence/RaceWriteCoordinator.cs:115-140`), but that lock alone does not prove every queue, schedule, import, or administrative writer is fenced. The deployment workflow’s pause/drain/stop/backup gates must be reconciled with every migration writer before applying.
- Useful regression analogues include `tests/HorseRacingPrediction.Api.Tests/HorseIdentityRepairEndpointsTests.cs`, `RaceAssignmentRepairTests.cs`, `RaceEndpointsTests.cs`, `tests/HorseRacingPrediction.Scraping.Tests/Jra/JraSourceIdentityTests.cs`, and `tests/HorseRacingPrediction.Infrastructure.Tests/SqliteDbContextProviderTests.cs`. These establish nearby behaviors, not migration completeness.

The user accepted the exact three pairs on 2026-10-14. They remain hypotheses, not identity proof; more pairs require explicit acknowledgement. No further source lookup is required for pure manifest validation. All 47 source mappings guide later typed coverage, not a target-data census.

## Bounded operating limits proposed for implementation

- At most 16 unique old Horse IDs per manifest; each destination identity is a canonical `jra-horse:` plus ten ASCII digits. Before `BuildHorseId`, the shared `JraSourceIdentity` helper must round-trip the identity exactly. The caller cannot provide a destination ID and name fallback is impossible for invalid input.
- At most 128 roots/references in a page, keyset continuation only; no `OFFSET`, full-store materialization, or per-horse N+1 reads.
- Preview HTTP call: one global immediate-admission slot, zero waiters, 30-second deadline, cancellation propagated. A busy request is rejected, not queued.
- JSON-backed source fields: check stored byte length before materialization (64 KiB maximum) and validate depth (16 maximum). A malformed, oversized, or unknown affected shape blocks the whole operation before any write.
- Preview request and response each stay within the frozen 1 MiB envelope. The separate staged operational journal has the frozen 64 MiB hard budget and 30-day terminal retention; never purge active/partial records. Reject staging if capacity cannot be reserved.
- Offline apply uses bounded per-store transactions/pages and durable checkpoints; it is not one cross-database transaction. Any incomplete checkpoint remains paused and requires explicit recovery; never auto-retry or resume.

## Ordered implementation slices and exact candidate ownership

All paths below are proposals for Lead’s pre-edit freeze. Tests are created alongside each slice. Each task is serialized; no overlapping source owner.

| Task | Scope and candidate files | Implementation order / proof |
| --- | --- | --- |
| T1a — pure manifest contract and policy (current) | `src/HorseRacingPrediction.Contracts/Identity/HorseIdentityMigrationManifest.cs`; `src/HorseRacingPrediction.Api/Identity/HorseIdentityMigrationManifestPolicy.cs`; `tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationManifestPolicyTests.cs`; this execution plan | Immutable bounded input; canonical ID validation; explicit hypothesis acceptance; exact shared-generator derivation; duplicate/conflict/self/cycle rejection. No DB access or root-existence claim. |
| T1b — typed read-only preview model | Focused contracts/service under `src/HorseRacingPrediction.Contracts/Identity/` and `src/HorseRacingPrediction.Api/Identity/`; `tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationPreviewTests.cs` | Inventory configured schema and typed references, preview root/target-stream collisions and counts, label unsupported/unavailable surfaces, and return blockers. Every affected row shape is typed, proven unchanged, or blocking. Preview cannot mutate source data and must not resolve queue/scheduler constructors that initialize stores. |
| T1c — typed transform and isolated persistence proof | Narrow migration reader/writer in `src/HorseRacingPrediction.Infrastructure/Persistence/` or owning store project selected after model inspection; Infrastructure and owning API/store tests | Explicit entity/event transformations using isolated SQLite/EventFlow stores. Test distinct same-name sources, missing old root, existing target stream, duplicate/unknown event, malformed/oversized typed JSON, uniqueness collision, derived race-entry preservation, reference closure, unrelated fields unchanged, pre-write failure, interruption/idempotence, and snapshots. Unsupported affected rows block before writes. |
| T2 — one-shot offline CLI and reservation lock | New early-mode parser/runner in `src/HorseRacingPrediction.Api/` plus API tests; exact entry point frozen after T1 | Parse only fixed migration mode plus opaque operation GUID before web hosting, migrators, consumers, schedulers, or workers. Use server-configured paths only. Acquire a process/file lock, validate the staged reservation and all preconditions, then run T1c in bounded pages. Invalid CLI exits before web startup. Test duplicate invocation, restart/cancellation, stale reservation, and no reads before ownership validation. |
| T3 — authenticated request/status UI and journal | Existing authenticated API/`AdminApiClient` patterns; dedicated `src/HorseRacingPrediction.Api/Web/Components/Pages/DataMigrationPreview.razor` and `.razor.css`; one Settings link; API/client/component/browser tests | Keep three routes from README. Preview reads; stage writes only to the separate bounded journal and returns `AwaitingActions`; status reads that journal only. Do not start CLI from the browser/API request. Show stale/partial/interrupted states. Test redaction, API restart, browser close/revisit, duplicate stage, busy/unavailable stores, 400/409/429/503, keyboard/mobile, and no auto-fetch/poll. |
| T4 — workflow operation handoff | `.github/workflows/app-deploy.yml` only if required, with dedicated workflow regression/validation | Add a required opaque operation-GUID input to the existing workflow, validate it and bind it to one staged journal operation. Preserve pause/drain/stop/WAL/backup/restart-paused behavior. Verify backup coverage for every configured DB and sidecar, not just current named subsets. CLI failure leaves the pipeline paused and API stopped or safely restarted paused; no automatic resume. Existing GitHub runner credentials remain in workflow; no new secret or local AWS login. |

## Verification sequence and expected results

T1a focused test during implementation:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~HorseIdentityMigrationManifestPolicy
```

Expected: all manifest policy tests pass, including invalid/duplicate/conflicting entries, name-only non-inference, same-name/different-source distinction, bounded construction, and shared-generator destination equivalence. T1b/T1c add focused API and Infrastructure filters named `FullyQualifiedName~HorseIdentityMigration`; expected real SQLite/EventFlow tests pass with zero source writes during preview and complete closure for accepted typed rows. Include the existing identity diagnostic and repair regressions:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~HorseIdentityMigration|FullyQualifiedName~HorseIdentityDiagnostic|FullyQualifiedName~HorseIdentityRepair|FullyQualifiedName~RaceAssignmentRepair"
dotnet test tests/HorseRacingPrediction.Infrastructure.Tests/HorseRacingPrediction.Infrastructure.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~HorseIdentityMigration
dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~JraSourceIdentity
dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --configuration Release --no-restore
dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes
```

Expected: focused tests and API Release build pass; formatter exits 0. Before integrated acceptance, run the matching Windows and Ubuntu app-ci full solution Release suite and exact formatter, workflow validation, isolated backup/restore and interruption drills, then the normal deployment workflow on the exact approved SHA. Terraform/backend/apply must not be simulated locally. Report remote workflow IDs, health, paused state, schema/data verification, and manual resume as separate evidence. Never claim tests or production verification from this plan; no tests were run for this document-only readiness task.

## T1a implementation evidence and remaining plan

1. Exact three pairs and manual workflow dispatch are accepted; identities remain hypotheses and explicit acceptance is recorded per manifest entry.
2. Journal budget is frozen at 64 MiB total, 1 MiB request/result and 30-day terminal retention; active or partial operations are never purged.
3. T1b/T1c must cover every affected typed store shape; unknown affected data blocks. Pure T1a cannot know whether an old root exists, so root existence belongs to preview.
4. T4 implements and tests exact backup/WAL/fencing and restart interlock; production evidence remains T5, without a local AWS prerequisite.
5. T1a is limited to immutable bounded input and pure validation/ID derivation. It does not persist or authorize CLI/API operations.

T1a result: added the immutable manifest contract, pure policy, and 8 policy tests. Construction copies at most 17 items (16 plus overflow sentinel); invalid manifests expose no accepted mappings. Destination IDs are server-derived after the shared normalizer round-trips the accepted identity. Tests pin the Namu and Nova observed URL-derived IDs. Root existence is deferred to read-only preview.

| Check | Command/result |
| --- | --- |
| Focused policy | `dotnet test ...Api.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~HorseIdentityMigrationManifestPolicy` — 8 passed, 0 failed (session 58095). |
| Existing API identity regressions | `dotnet test ...Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~HorseIdentityDiagnostic|FullyQualifiedName~HorseIdentityRepair|FullyQualifiedName~RaceAssignmentRepair"` — 59 passed, 0 failed (session 77393). |
| Shared JRA identity regressions | `dotnet test ...Scraping.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~JraSourceIdentity` — 9 passed, 0 failed. |
| API Release build | `dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --configuration Release --no-restore` — exit 0, 0 warnings, 0 errors. |
| Exact solution formatter | `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` — session 41662, exit 0. |
| CodeGraph | `codegraph sync .` — exit 0, already up to date; query confirmed the policy call path and test coverage. |
| Diff/status | `git diff --check` — exit 0. Final status confirms only the three T1a implementation files and this execution plan are T1a-owned changes; pre-existing user/Lead changes are preserved. |

Failure ledger: the first focused run failed before tests executed with six CS8752 errors from target-typed `new(...)` in params-array calls. Replacing them with explicit constructors fixed the cause; an intermediate seven-test run passed before bounded snapshot and pinned-identity assertions were added. The current final focused run is 8/8. No source change escaped the three P1a files.

T1a is the only code scope implemented in this checkpoint. T1b/T1c/T2/T3/T4 and all production operations remain gated; this does not verify or complete the parent migration.

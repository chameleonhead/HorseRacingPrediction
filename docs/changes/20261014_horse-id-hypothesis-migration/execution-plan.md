# T1 readiness and ordered implementation plan

Status: source-informed readiness. Lead accepted the P1a pure-policy plan after the user's exact three-pair/operation-boundary approval; source remains unchanged at this note. Later database/runner/UI scopes remain serialized and gated. The request is the bounded physical migration in [README.md](README.md), not a name-repair or general-purpose framework.

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

- At most 16 unique old Horse IDs per manifest; each destination identity is a canonical `jra-horse:` plus ten ASCII digits and the destination ID is derived by `BuildHorseId`.
- At most 128 roots/references in a page, keyset continuation only; no `OFFSET`, full-store materialization, or per-horse N+1 reads.
- Preview HTTP call: one global immediate-admission slot, zero waiters, 30-second deadline, cancellation propagated. A busy request is rejected, not queued.
- JSON-backed source fields: check stored byte length before materialization (64 KiB maximum) and validate depth (16 maximum). A malformed, oversized, or unknown affected shape blocks the whole operation before any write.
- Preview response stays within 1 MiB. Lead froze separate journal hard budget at 64 MiB, with 30-day eligible terminal retention; never purge active/partial records. Reject admission if capacity cannot be reserved.
- Offline apply uses bounded per-store transactions/pages and durable checkpoints; it is not one cross-database transaction. Any incomplete checkpoint remains paused and requires explicit recovery; never auto-retry or resume.

## Ordered implementation slices and exact candidate ownership

All paths below are proposals for Lead’s pre-edit freeze. Tests are created alongside each slice. Each task is serialized; no overlapping source owner.

| Task | Scope and candidate files | Implementation order / proof |
| --- | --- | --- |
| P1a — manifest contract and policy | `src/HorseRacingPrediction.Contracts/Identity/HorseIdentityMigrationManifest.cs`; `src/HorseRacingPrediction.Api/Identity/HorseIdentityMigrationManifestPolicy.cs`; `tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationManifestPolicyTests.cs` | Immutable list of at most 16 unique old IDs and canonical JRA identities; derive destination internally; reject bad format, duplicate old IDs, conflicting identity for one old ID, duplicate destinations, self-map, cycles, and unknown IDs. Include explicit `Hypothesis`/operator-accepted state; never infer acceptance. No DB access. |
| P1b — typed read-only preview model | New focused contracts/service under `src/HorseRacingPrediction.Contracts/Identity/` and `src/HorseRacingPrediction.Api/Identity/`; `tests/HorseRacingPrediction.Api.Tests/HorseIdentityMigrationPreviewTests.cs` | Inventory configured schema and typed references, preview root/target-stream collisions and counts, label every unsupported/unavailable surface, and return blockers. Reconcile the full source-mapped allowlist at runtime; every affected row shape is either typed, verified unchanged, or blocking. Preview cannot save or mutate source data. Use existing reader/options patterns without resolving queue/scheduler constructors that initialize stores. |
| P1c — typed transform and isolated persistence proof | New narrowly scoped migration reader/writer in `src/HorseRacingPrediction.Infrastructure/Persistence/` or the owning store project selected after model inspection; tests in `tests/HorseRacingPrediction.Infrastructure.Tests/` and the owning API/store test project | Add only explicit entity/event transformations. Use real isolated SQLite/EventFlow stores and snapshots. Test same-name distinct sources, old root missing, target root already present, duplicate/unknown event, typed JSON malformed/oversized, unique-index collision, derived race-entry ID preservation, exact count/reference closure, no-op unrelated rows, failure before first write, interruption/idempotence, and before/after snapshots. If any affected row cannot be proven, stop before writes. |
| T2 — one-shot offline CLI and reservation lock | New early-mode parser/runner in `src/HorseRacingPrediction.Api/` plus API tests; exact entry point frozen after T1 | Parse only a fixed migration mode plus opaque operation GUID before normal web host, store migrators, queue consumers, schedulers, and background services start. Read DB paths from server configuration only. Acquire a process/file lock, validate durable reservation, revalidate preview and all preconditions, then run P1c in bounded pages. Invalid CLI exits before web startup. Test duplicate invocation, child/API restart, process-tree cancellation, stale reservation, and no source read before lock/validation. |
| T3 — authenticated request/status UI and journal | Existing authenticated API/`AdminApiClient` patterns; dedicated `src/HorseRacingPrediction.Api/Web/Components/Pages/DataMigrationPreview.razor` and `.razor.css`; one Settings link; API/client/component/browser tests | Keep three routes from README. Preview reads; stage writes only to the separate bounded journal and returns `AwaitingActions`; status reads that journal only. Do not start CLI from the browser/API request. Show stale/partial/interrupted states. Test redaction, API restart, browser close/revisit, duplicate stage, busy/unavailable stores, 400/409/429/503, keyboard/mobile, and no auto-fetch/poll. |
| T4 — workflow operation handoff | `.github/workflows/app-deploy.yml` only if required, with dedicated workflow regression/validation | Add a required opaque operation-GUID input to the existing workflow, validate it and bind it to one staged journal operation. Preserve pause/drain/stop/WAL/backup/restart-paused behavior. Verify backup coverage for every configured DB and sidecar, not just current named subsets. CLI failure leaves the pipeline paused and API stopped or safely restarted paused; no automatic resume. Existing GitHub runner credentials remain in workflow; no new secret or local AWS login. |

## Verification sequence and expected results

P1a focused test during implementation:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~HorseIdentityMigrationManifestPolicy
```

Expected: all manifest policy tests pass, including invalid/duplicate/conflicting entries, name-only non-inference, same-name/different-source distinction, and shared-generator destination equivalence. P1b/P1c add focused API and Infrastructure filters named `FullyQualifiedName~HorseIdentityMigration`; expected real SQLite/EventFlow tests pass with zero source writes during preview and complete closure for accepted typed rows. Include the existing identity diagnostic and repair regressions:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~HorseIdentityMigration|FullyQualifiedName~HorseIdentityDiagnostic|FullyQualifiedName~HorseIdentityRepair|FullyQualifiedName~RaceAssignmentRepair"
dotnet test tests/HorseRacingPrediction.Infrastructure.Tests/HorseRacingPrediction.Infrastructure.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~HorseIdentityMigration
dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~JraSourceIdentity
dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --configuration Release --no-restore
dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes
```

Expected: focused tests and API Release build pass; formatter exits 0. Before integrated acceptance, run the matching Windows and Ubuntu app-ci full solution Release suite and exact formatter, workflow validation, isolated backup/restore and interruption drills, then the normal deployment workflow on the exact approved SHA. Terraform/backend/apply must not be simulated locally. Report remote workflow IDs, health, paused state, schema/data verification, and manual resume as separate evidence. Never claim tests or production verification from this plan; no tests were run for this document-only readiness task.

## Lead readiness disposition

1. Exact three pairs and manual workflow dispatch are explicitly accepted; identities remain hypotheses.
2. P1b/P1c must inspect and implement all affected typed surfaces; unknown affected shape blocks. This does not block independent P1a.
3. Journal budget frozen above; reservation-before-receipt is required, no invented disk-capacity guarantee.
4. T4 implements and tests exact backup/WAL/fencing and restart interlock; actual production evidence remains T5, no local AWS prerequisite.
5. P1a pure policy has no persistence and therefore cannot reject a nonexistent root: it validates identifier shape only; root existence is later preview responsibility. Preserve normal deploy input behavior; GUID is migration-mode-only.

Lead releases only the three exact P1a paths after the active coding audit/root audit gate passes. No production operation is authorized by this readiness note; approved operations still require their technical gates. No source tests were run for readiness.

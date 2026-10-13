# M7 execution plan

Proposal only. The canonical AC/task/routing/audit/result-metrics table is in the [README](README.md#task-plan); do not maintain a second task ledger here. M7 is read-only evidence acquisition, not a migration executor or fixed apply manifest. Parent owns target census, identity mapping, writer fence, backup/rollback, and cutover.

## Repository facts

- The M2 persistence inventory maps 20 EventStore, 25 CollectionPlatform, 1 PredictionSchedule, and 1 optional LocalCollectionQueue table. The [reference ledger](reference-scope.md) lists all 47 source tables, stable keysets, typed roles, and unsupported cases. Source mappings do not prove deployed schema or rows.
- `EndpointExtensions.MapApiEndpoints` registers the API-key-protected `adminReadGroup`. The browser uses server-side `AdminApiClient`; it never receives the API key. Existing repair routes and their consumers are enumerated in README and remain unchanged.
- `Program.cs` does normal web startup; maintenance mode is not a full writer fence. `HorseRacingPrediction.CollectionCutover` handles legacy job-file behavior, not a full-data migration.
- Compose already mounts `/data` for API state. The proposed `/data/migration-preview-journal/operations.db` sits outside the four source databases and uses the existing service identity. No `USER` directive establishes a non-root runtime identity; this proposal does not change identity or add a mount/privilege. Journal ACL, reparse/symlink, launch, and process-tree behavior require Linux/Windows tests.

## Fixed command and ownership protocol

Invoke the existing API binary in a fixed mode before `WebApplication.CreateBuilder`:

```text
dotnet HorseRacingPrediction.Api.dll --run-data-migration-preview <server-generated-operation-guid>
```

The API fixes executable/assembly paths and uses `ProcessStartInfo.ArgumentList`, never shell concatenation. The only user request value is a canonical RequestId GUID; operation GUID is server-generated. Child rejects unknown mode, count, or GUID and uses only configured store locations plus an explicit safe config allowlist. It does not construct the web host, resolve migration-owning stores, migrate schemas, register definitions, dispatch background workers, access SQS, or inherit API/AWS/token secrets unnecessarily.

The API atomically reserves RequestId/operation in the separate journal. Before opening any source file, the child acquires an exclusive OS lock on a fixed journal lock file and revalidates the durable reservation/owner/state. It holds the lock through the complete scan and releases it in `finally`. Same RequestId returns the same operation; another RequestId while reservation or lock is active returns immediate 429. A process-local semaphore or persisted PID/expiry lease is insufficient. After API restart, held lock means `StatusUnknown/Busy` and no second scanner. An unlocked nonterminal operation may be marked `Interrupted` only after transactional journal ownership revalidation; the user must explicitly request a fresh scan. Never auto-resume or kill by persisted PID. API cancellation may terminate only its retained child process handle/tree, and terminalize only after observing lock release. Windows/Linux tests must cover duplicate starts, restart while child lives, killed child/lock release, lock contention before source open, journal corruption, and lost reservation.

## Journal, data, and response contract

The fixed journal path is `migration-preview-journal/operations.db` under configured `CollectionPlatform:StateDirectory` (production `/data`). Require existing-service-only ACL (Unix directory/file `0700`/`0600`; Windows service-only ACL); fail closed on permissions, symlink/reparse, path collision, or journal schema drift. The journal stores fixed states/reasons, IDs, UTC timestamps, bounded counters, schema/policy fingerprints, and allowlisted typed observation records only. No raw row/event/metadata JSON, URLs, SQL, source paths, actor, secret, cursor, or free-form exception. It is separate from all source stores.

State machine: `Accepted → Running → Completed | Incomplete | FailedSafe | Interrupted`. `Completed` means only that every configured supported table page reached its declared end; it is not target-complete or migration-ready. `Incomplete` is a valid outcome. Cursor binds operation, registered table, last typed key, policy version, persisted schema/surface fingerprint, and 30-minute expiry; max cursor 4 KiB. Items reads journaled observations only; it does not re-read live stores. Cursors, RequestIds, operation IDs, and source keys are never logged.

Fixed caps: 30-minute wall clock; 512 MiB monitored RSS high-water (not a hard OS guarantee); child GC heap hard limit 256 MiB; one 128-key page and bounded 64 KiB input buffers live; 10,000,000 aggregate keys; 1-second query timeout; 64 KiB text limit checked before materialization; JSON depth 16; 16 references/row; 1 MiB API page; 128 MiB/op journal; 1 GiB total journal including DB/WAL/SHM/temp files; 30-day terminal retention. Above 512 MiB sampled RSS becomes `Incomplete/MemoryBudgetReached`; measure peak. Reserve terminal-status bytes before each scan batch. On budget, time, row, parser, or response limit, stop incomplete and never silently drop rows or claim completion. Cleanup only expired terminal journal records; never vacuum/clean source databases.

For isolated fixtures, assert identical schema and allowlisted row snapshots before/after. For live stores, prove query-only/read-only access and no writes by this command, while explicitly allowing unrelated concurrent-writer changes; do not claim live byte-for-byte file identity. Source observations carry separate UTC timestamps and `IsNonAtomic=true`.

HTTP codes: catalogue GET is 200/401/503, no-store, and includes latest retained operation reference from journal without source reads; Start POST is 202 for new/idempotent RequestId, 400 invalid GUID, 401 auth, 429 conflicting active request, 503 journal admission unavailable. Child/source failure after 202 becomes terminal journal status, not POST 503. Status/items GET are 200/401/404/503 and no-store; malformed, expired, mismatched, or oversized cursor is 400 before journal/source IO. Any API-owned child termination waits for lock release before marking terminal.

## UI and source readers

Settings links to `/settings/data-migration-preview`; accepted receipt navigates to `/settings/data-migration-preview/{operationId:guid}`. Returning to Settings uses the latest retained operation reference from catalogue metadata. Mount/revisit may read status once but never starts a source scan. Start is disabled while an operation is active; Start/Refresh/Next are manual. No timer/polling, page accumulation, Apply, Execute, Resume, Reset, or job Retry. Failed continuation keeps old result marked stale. No browser-owned operation lifetime.

`reference-scope.md` is the exact 47-table source key/evidence map. Reconcile physical objects and column shape before row reads. Scan each table independently by its declared key using parameterized, bounded batches; no OFFSET, N+1, unbounded materialization, cross-store UNION, guessed key, or silent skip. Unknown schema/payload, noncanonical key, duplicate/malformed event, missing store, and external surface become explicit incomplete/unknown/not-inspected status, not zero.

## Verification to require after approval

No test/build was run for this proposal. The Approved implementation task table must retain these counterexamples and exact project paths, then run at minimum:

```powershell
dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MigrationPreview
dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MigrationPreview
dotnet build HorseRacingPrediction.sln -c Release --no-restore
dotnet test HorseRacingPrediction.sln --no-build --configuration Release --collect:"XPlat Code Coverage" --filter "TestCategory!=External" --logger "trx;LogFileName=app-ci.trx"
dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes
git diff --check
```

Required evidence: all-47 schema/keyset and typed-reader matrix; same-name/different-source and missing-source cases; malformed/unknown/oversized row; orphan references; 129th key and query-budget proof; isolated SQLite no-write and query-only rejection; live concurrent-writer caveat; missing/optional store; journal cap/WAL accounting; duplicate RequestId/start; cross-process lock and API restart with child alive; child kill/lock release; reservation loss; ACL/symlink/path collision; cursor expiry/tamper; 503 then same-operation recovery; memory high-water; timeout/process-tree cleanup; actual AdminApiClient HTTP statuses; UI stale/revisit/manual behavior; Windows/Linux process semantics. Also revalidate repository CI scripts, empty-DB/model and dependency checks, Playwright prerequisites, full CI, and remote deployment workflow. Local Windows is not Linux parity.

Parent migration sufficiency remains open. External queues/DLQ, files, backups, model storage, host state and target-only objects stay explicitly not inspected. A successful scan is not an apply manifest and never authorizes data mutation.

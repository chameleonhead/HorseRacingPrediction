using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
[TestCategory("Rc5Measurement")]
public sealed class CollectionDispatchRc5MeasurementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly CollectionDefinitionId DispatchDefinition = new("rc5-dispatch");
    private static readonly CollectionDefinitionId ScheduleDefinition = new("rc5-schedule");
    private const int MeasuredDispatchCycles = 100;
    private const int DefaultMeasuredProducerCycles = 10;
    private const int MaximumDiagnosticProducerCycles = 200;
    private const int WarmupCycles = 1;

    private static int MeasuredProducerCycles => ParseMeasuredProducerCycles(
        Environment.GetEnvironmentVariable("RC5_PRODUCER_CYCLES"));
    private static bool CaptureDiagnosticSamples =>
        string.Equals(Environment.GetEnvironmentVariable("RC5_CAPTURE_RAW_SAMPLES"), "1", StringComparison.Ordinal);
    private static bool StatusRecorderControlOff =>
        string.Equals(Environment.GetEnvironmentVariable("RC5_STATUS_RECORDER"), "off", StringComparison.OrdinalIgnoreCase);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod] public Task Empty() => RunWorkloadAsync("empty");
    [TestMethod] public Task CapacityFull() => RunWorkloadAsync("capacity-full");
    [TestMethod] public Task AllLanesReady() => RunWorkloadAsync("all-lanes-ready");
    [TestMethod] public Task DenseActivePrefix() => RunWorkloadAsync("dense-active-prefix");
    [TestMethod] public Task HeldPrefix() => RunWorkloadAsync("held-prefix");
    [TestMethod] public Task FixedLiveHistory1x() => RunWorkloadAsync("fixed-live-history-1x");
    [TestMethod] public Task FixedLiveHistory10x() => RunWorkloadAsync("fixed-live-history-10x");

    [TestMethod]
    public void ProducerCycleOptionDefaultsAndValidatesBounds()
    {
        Assert.AreEqual(DefaultMeasuredProducerCycles, ParseMeasuredProducerCycles(null));
        Assert.AreEqual(DefaultMeasuredProducerCycles, ParseMeasuredProducerCycles(""));
        Assert.AreEqual(10, ParseMeasuredProducerCycles("10"));
        Assert.AreEqual(MaximumDiagnosticProducerCycles, ParseMeasuredProducerCycles("200"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ParseMeasuredProducerCycles("9"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ParseMeasuredProducerCycles("201"));
        Assert.Throws<ArgumentException>(() => ParseMeasuredProducerCycles("many"));
        Assert.AreEqual("baseline", ParseFixtureVariant("baseline"));
        Assert.AreEqual("current", ParseFixtureVariant("current"));
        Assert.AreEqual(0, ExpectedScheduleTasksPerCycle("dense-active-prefix", "baseline"));
        Assert.AreEqual(32, ExpectedScheduleTasksPerCycle("dense-active-prefix", "current"));
        Assert.AreEqual("0decdedfad1d3a0dedb07a3f8b6cabf5632a3ca7",
            ParseRevisionLabel("0decdedfad1d3a0dedb07a3f8b6cabf5632a3ca7"));
        Assert.Throws<ArgumentException>(() => ParseFixtureVariant(null));
        Assert.Throws<ArgumentException>(() => ParseFixtureVariant("unknown"));
    }

    [TestMethod]
    public void DirectScheduleDiagnosticOptionAndFixtureGuardsAreFailClosed()
    {
        Assert.IsFalse(ParseDirectScheduleRequestControl(null));
        Assert.IsFalse(ParseDirectScheduleRequestControl("false"));
        Assert.IsTrue(ParseDirectScheduleRequestControl("true"));
        Assert.Throws<ArgumentException>(() => ParseDirectScheduleRequestControl("yes"));
        Assert.Throws<InvalidOperationException>(() => ValidateDirectScheduleDiagnostic(true,
            "fixed-live-history-1x", "current"));
        Assert.Throws<InvalidOperationException>(() => ValidateDirectScheduleDiagnostic(true,
            "all-lanes-ready", "baseline"));
        Assert.Throws<InvalidOperationException>(() => ValidateDirectScheduleFixtureManifest(
            new("all-lanes-ready", "fixture", 30, 19, 64, 30, 0, 0, 0,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), "resolver", "fixture")));
        Assert.Throws<InvalidOperationException>(() => ValidateDirectScheduleFixtureManifest(
            new("all-lanes-ready", "fixture", 30, 20, 64, 30, 0, 0, 1,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), "resolver", "fixture")));
        Assert.IsFalse(CandidateIsHeldByActiveRepairHolds(["race-live"], ["race-unrelated"]));
        Assert.IsTrue(CandidateIsHeldByActiveRepairHolds(["race-live"], ["race-live"]));
        Assert.IsTrue(CandidateIsHeldByActiveRepairHolds([null], ["race-unrelated"]));
        Assert.IsFalse(CandidateIsHeldByActiveRepairHolds([null], Array.Empty<string>()));
    }

    [TestMethod]
    public async Task TrackingCounterIncludesAddedEntityWithoutMaterialization()
    {
        var directory = CreateDirectory("tracking-counter");
        try
        {
            var path = Path.Combine(directory, "tracking.db");
            using var metrics = new CollectionDispatchRc5MeasurementInstrumentation();
            var options = BuildDbOptions(BuildConnectionString(path), metrics);
            _ = new CollectionPlatformStore(options);
            await using var db = new CollectionPlatformDbContext(options);
            await db.Database.OpenConnectionAsync();
            metrics.Reset();

            using (metrics.BeginMeasurementWindow())
            {
                db.Definitions.Add(new CollectionDefinitionEntity
                {
                    DefinitionId = "rc5-tracking-counter",
                    Name = "RC5 tracking counter",
                    ResourceType = CollectionResourceType.Race,
                    CurrentRevision = 1,
                    Enabled = true,
                });
                await db.SaveChangesAsync();
            }

            var snapshot = metrics.Snapshot();
            Assert.AreEqual(0L, snapshot.EntityMaterializations);
            Assert.IsTrue(snapshot.TrackedEntities > 0, "Added-only entities must contribute to tracked counts.");
            Assert.AreEqual(0L, snapshot.NativeObserverErrors);
            Assert.IsTrue(snapshot.Total.SqlStatements > 0, "SQLite trace must observe commands on a measured context.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task SqliteProfileCallbackCountsCompletedStatementsWhenOptedIn()
    {
        var directory = CreateDirectory("sqlite-profile-counter");
        try
        {
            var path = Path.Combine(directory, "profile.db");
            using var metrics = new CollectionDispatchRc5MeasurementInstrumentation(nativeSqliteProfileEnabled: true);
            var options = BuildDbOptions(BuildConnectionString(path), metrics);
            _ = new CollectionPlatformStore(options);
            await using var db = new CollectionPlatformDbContext(options);
            await db.Database.OpenConnectionAsync();

            using (metrics.BeginMeasurementWindow())
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT 1";
                _ = await command.ExecuteScalarAsync();
            }

            var profiles = metrics.Snapshot().SqlProfiles;
            Assert.IsTrue(profiles.Count > 0, "Opted-in SQLITE_TRACE_PROFILE callbacks must be observable.");
            Assert.IsTrue(profiles.All(profile => profile.ExecutionCount > 0 && profile.TotalElapsedNanoseconds >= 0
                && profile.MaxElapsedNanoseconds >= 0));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task TempWorklistInsertFromEfQueryPreservesParametersAndRollbackCleanup()
    {
        var directory = CreateDirectory("sqlite-temp-worklist-probe");
        try
        {
            var connectionString = BuildConnectionString(Path.Combine(directory, "worklist.db"));
            var options = BuildDbOptions(connectionString);
            var store = new CollectionPlatformStore(options);
            await SeedDefinitionsAsync(options);
            foreach (var resourceId in new[] { "rc5-worklist-a", "rc5-worklist-b", "rc5-worklist-excluded" })
                await store.RequestAsync(new(CollectionResourceType.Race, "JRA", resourceId), DispatchDefinition,
                    1, CollectionReason.Initial, Now);

            await using var db = new CollectionPlatformDbContext(options);
            await db.Database.OpenConnectionAsync();
            var selectedResourceIds = new[] { "rc5-worklist-a", "rc5-worklist-b" };
            var expectedResourcePks = await db.Resources.AsNoTracking()
                .Where(resource => selectedResourceIds.Contains(resource.ResourceId))
                .Select(resource => resource.ResourcePk).Distinct().OrderBy(resourcePk => resourcePk)
                .ToArrayAsync();
            Assert.AreEqual(2, expectedResourcePks.Length);

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TEMP TABLE HeldResourceSnapshot (ResourcePk INTEGER PRIMARY KEY, IsHeld INTEGER NOT NULL);");
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TEMP TABLE HeldResourceWorklist (ResourcePk INTEGER PRIMARY KEY);");
                var pageQuery = db.Resources.AsNoTracking()
                    .Where(resource => selectedResourceIds.Contains(resource.ResourceId)
                        && resource.ResourcePk > 0)
                    .Select(resource => resource.ResourcePk)
                    .Distinct().OrderBy(resourcePk => resourcePk).Take(128);
                await using var insert = pageQuery.CreateDbCommand();
                Assert.AreSame(db.Database.GetDbConnection(), insert.Connection,
                    "The provider command must stay attached to the operation connection.");
                Assert.IsTrue(insert.Parameters.Count > 0,
                    "The EF-generated source query must carry bound filter parameters into the insert.");
                insert.Transaction = transaction.GetDbTransaction();
                insert.CommandText = "INSERT OR IGNORE INTO temp.HeldResourceWorklist (ResourcePk) " + insert.CommandText;
                await insert.ExecuteNonQueryAsync();

                var actualResourcePks = await ReadTempWorklistAsync(db);
                CollectionAssert.AreEqual(expectedResourcePks, actualResourcePks);
                await transaction.RollbackAsync();
            }

            Assert.AreEqual(0, await CountTemporaryWorklistTablesAsync(db),
                "Rollback must remove both TEMP tables created by the operation transaction.");

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TEMP TABLE HeldResourceSnapshot (ResourcePk INTEGER PRIMARY KEY, IsHeld INTEGER NOT NULL);");
                await Assert.ThrowsExactlyAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
                    "CREATE TEMP TABLE HeldResourceWorklist ("));
                await db.Database.ExecuteSqlRawAsync(
                    "DROP TABLE IF EXISTS temp.HeldResourceWorklist; DROP TABLE IF EXISTS temp.HeldResourceSnapshot;");
                await transaction.CommitAsync();
            }
            Assert.AreEqual(0, await CountTemporaryWorklistTablesAsync(db),
                "Cleanup after worklist creation failure must remove the already-created snapshot table.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<long[]> ReadTempWorklistAsync(CollectionPlatformDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT ResourcePk FROM temp.HeldResourceWorklist ORDER BY ResourcePk;";
        var values = new List<long>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetInt64(0));
        return values.ToArray();
    }

    private static async Task<int> CountTemporaryWorklistTablesAsync(CollectionPlatformDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_temp_master WHERE type = 'table' "
            + "AND name IN ('HeldResourceSnapshot', 'HeldResourceWorklist');";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [TestMethod]
    public async Task RawSecondConnectionWriterLockIsObservedAndRetryCreatesExactlyOnce()
    {
        var directory = CreateDirectory("raw-writer-lock");
        try
        {
            var database = Path.Combine(directory, "lock.db");
            var connectionString = BuildConnectionString(database, 1);
            var options = BuildDbOptions(connectionString);
            var store = new CollectionPlatformStore(options);
            var key = new ResourceKey(CollectionResourceType.Race, "JRA", "rc5-lock-resource");
            await SeedDefinitionsAsync(options);

            await using var independentWriter = new SqliteConnection(connectionString);
            await independentWriter.OpenAsync();
            await using var blockingTransaction = independentWriter.BeginTransaction(deferred: false);
            var wait = Stopwatch.StartNew();
            var lockFailure = await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
                await store.RequestAsync(key, DispatchDefinition, 1, CollectionReason.Initial, Now));
            wait.Stop();

            Assert.AreEqual(5, lockFailure.SqliteErrorCode,
                "The independent raw connection must block the Store through SQLite itself, not only its in-process gate.");
            await blockingTransaction.CommitAsync();
            var receipt = await store.RequestAsync(key, DispatchDefinition, 1, CollectionReason.Initial, Now);
            Assert.IsTrue(receipt.CreatedTask);
            Assert.AreEqual(1, (await store.GetTasksAsync()).Count(x => x.Resource.Id == key.Id));
            TestContext.WriteLine("RC5_LOCK_JSON:" + JsonSerializer.Serialize(new
            {
                fixture = "raw-second-connection-writer-lock",
                sqliteErrorCode = lockFailure.SqliteErrorCode,
                sqliteErrorName = lockFailure.SqliteExtendedErrorCode,
                blockedMilliseconds = wait.Elapsed.TotalMilliseconds,
                retryCreatedTask = receipt.CreatedTask,
                durableTaskCount = 1,
            }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private async Task RunWorkloadAsync(string scenario)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_COLLECTION_RC5_MEASUREMENTS"), "1",
                StringComparison.Ordinal))
            Assert.Inconclusive("Set RUN_COLLECTION_RC5_MEASUREMENTS=1 to run the explicit RC5 comparison harness.");

        var fixtureVariant = ParseFixtureVariant(Environment.GetEnvironmentVariable("RC5_FIXTURE_VARIANT"));
        var revision = ParseRevisionLabel(Environment.GetEnvironmentVariable("RC5_REVISION"));
        var directScheduleRequests = ParseDirectScheduleRequestControl(
            Environment.GetEnvironmentVariable("RC5_SCHEDULE_DIRECT_REQUESTS"));
        ValidateDirectScheduleDiagnostic(directScheduleRequests, scenario, fixtureVariant);
        if (directScheduleRequests && (CollectionDispatchRc5MeasurementInstrumentation.NativeTraceEnabledFromEnvironment
            || !StatusRecorderControlOff))
            throw new InvalidOperationException(
                "The direct RequestAsync diagnostic requires SQLite tracing and the runtime recorder to be disabled.");

        var directory = CreateDirectory("rc5-" + scenario);
        try
        {
            var template = Path.Combine(directory, "template.db");
            var sample = Path.Combine(directory, "sample.db");
            var seed = await CreateFixtureAsync(template, scenario);
            var sqliteVersion = ReadSqliteVersion(template);
            var operationMetrics = new List<CollectionDispatchRc5ActionResult>();

            operationMetrics.Add(await MeasureDispatcherAsync(template, sample, directory, scenario, seed));
            operationMetrics.Add(await MeasureScheduleAsync(template, sample, directory, scenario, seed,
                fixtureVariant, revision, directScheduleRequests));
            operationMetrics.Add(await MeasureRecoveryAsync(template, sample, directory, scenario, seed));

            if (scenario is "held-prefix" or "fixed-live-history-1x" or "fixed-live-history-10x")
            {
                var dispatcher = operationMetrics.Single(x => x.Action == "dispatcher");
                var eligiblePending = scenario == "held-prefix" ? 8 : 23;
                Assert.IsTrue(dispatcher.QueueSends > 0,
                    "Held/history fixtures must demonstrate unheld work behind the held prefix.");
                if (string.Equals(Environment.GetEnvironmentVariable("ASSERT_COLLECTION_RC5_OPTIMIZATIONS"), "1",
                        StringComparison.Ordinal))
                {
                    Assert.IsTrue(dispatcher.QueueSends >= MeasuredDispatchCycles,
                        "Every current held/history cycle must dispatch at least one unheld resource behind the prefix.");
                    Assert.IsTrue(dispatcher.QueueSends <= MeasuredDispatchCycles * eligiblePending,
                        "Queue sends cannot exceed the fixture's exact unheld pending-resource count.");
                }
            }

            var output = new
            {
                schemaVersion = 1,
                scenario,
                revision,
                fixtureVariant,
                observerMode = CollectionDispatchRc5MeasurementInstrumentation.NativeTraceEnabledFromEnvironment
                    ? "sqlite-trace-v2-enabled" : "sqlite-trace-v2-disabled-control",
                sqlAndRowMetricsAvailable = CollectionDispatchRc5MeasurementInstrumentation.NativeTraceEnabledFromEnvironment,
                sqlProfileAvailable = CollectionDispatchRc5MeasurementInstrumentation.NativeTraceEnabledFromEnvironment
                    && CollectionDispatchRc5MeasurementInstrumentation.NativeProfileEnabledFromEnvironment,
                runtime = new
                {
                    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    sqliteVersion,
                    gcServer = System.Runtime.GCSettings.IsServerGC,
                },
                fixture = seed,
                scheduleExecutionMode = directScheduleRequests
                    ? "diagnostic-direct-RequestAsync-scheduler-eligibility-bypass"
                    : "guarded-scheduler-default",
                measuredDispatchCycles = MeasuredDispatchCycles,
                measuredProducerAndRecoveryCycles = MeasuredProducerCycles,
                warmupCyclesPerAction = WarmupCycles,
                latencyScopeNote = "P50/P95 and allocations are per-action measurements, not full hosted-loop end-to-end costs; action scope and recorder mode are reported per result.",
                actions = CollectionDispatchRc5MeasurementInstrumentation.NativeTraceEnabledFromEnvironment
                    ? operationMetrics.ToArray()
                    : operationMetrics.Select(action => action with
                    {
                        Metrics = null,
                        DiagnosticTelemetryMetrics = null,
                        QueryPlans = null,
                    }).ToArray(),
            };
            TestContext.WriteLine("RC5_JSON:" + JsonSerializer.Serialize(output));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private async Task<CollectionDispatchRc5ActionResult> MeasureDispatcherAsync(string template, string sample,
        string directory, string scenario, CollectionDispatchRc5FixtureManifest fixture)
    {
        var metrics = CreateMeasurementInstrumentation();
        try
        {
            RestoreDatabase(template, sample);
            var resolver = new CountingRaceIdentityResolver();
            var store = CreateStore(sample, metrics, resolver);
            var telemetry = new MeasurementTelemetry(metrics);
            var queue = new MeasurementQueue();
            var options = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                MaxInFlightEnvelopes = scenario == "capacity-full" ? 1 : 32,
                EnvelopeMaxTasks = 1,
                AggregationDelayMilliseconds = 0,
            });
            var dispatcher = CreateDispatcher(store, queue, options, telemetry);
            SetCurrentTime(dispatcher);
            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            RestoreDatabase(template, sample);
            SetDispatcherTelemetrySnapshotTime(dispatcher, 0);
            queue.Reset();
            resolver.Reset();
            using var sampler = new WorkingSetSampler();
            var durations = new List<double>(MeasuredDispatchCycles);
            var allocations = new List<long>(MeasuredDispatchCycles);
            var snapshots = new List<CollectionDispatchRc5MetricsSnapshot>(MeasuredDispatchCycles);
            var resolverInputs = new List<int>(MeasuredDispatchCycles);
            var sent = new List<int>(MeasuredDispatchCycles);
            var diagnosticSamples = CaptureDiagnosticSamples
                ? new List<CollectionDispatchRc5DiagnosticSample>(MeasuredDispatchCycles) : null;
            for (var index = 0; index < MeasuredDispatchCycles; index++)
            {
                RestoreDatabase(template, sample);
                queue.Reset();
                resolver.Reset();
                metrics.Reset();
                var beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
                var stopwatch = Stopwatch.StartNew();
                using (metrics.BeginMeasurementWindow())
                using (sampler.MeasurementWindow())
                    await dispatcher.DispatchOnceAsync(CancellationToken.None);
                stopwatch.Stop();
                allocations.Add(GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated);
                durations.Add(stopwatch.Elapsed.TotalMilliseconds);
                var snapshot = metrics.Snapshot();
                snapshots.Add(snapshot);
                AssertMeasurementObserver(snapshot, metrics, "dispatcher", index);
                if (metrics.NativeSqliteTraceEnabled)
                {
                    Assert.AreEqual(0L, snapshots[^1].NativeObserverErrors,
                        "The native SQLite observer must not silently drop callback errors.");
                    Assert.IsTrue(snapshots[^1].Total.SqlStatements > 0,
                        "The native SQLite observer must capture SQL for each measured dispatcher cycle.");
                }
                resolverInputs.Add(resolver.Calls);
                sent.Add(queue.SendCount);
                Assert.AreEqual(ExpectedDispatchSendsPerCycle(scenario), queue.SendCount,
                    $"Unexpected dispatcher output in {scenario}, cycle {index}.");
                if (diagnosticSamples is not null)
                    diagnosticSamples.Add(CreateDiagnosticSample(index, stopwatch.Elapsed.TotalMilliseconds,
                        allocations[^1], queue.SendCount, null, resolver.Calls, null, snapshot, metrics));
            }

            var aggregate = Aggregate(snapshots);
            var operation = aggregate.Categories.GetValueOrDefault("operation", CollectionDispatchRc5MetricCounts.Empty);
            var telemetryMetrics = aggregate.Categories.GetValueOrDefault("diagnostic-telemetry", CollectionDispatchRc5MetricCounts.Empty);
            var assertCurrentOptimizations = string.Equals(
                Environment.GetEnvironmentVariable("ASSERT_COLLECTION_RC5_OPTIMIZATIONS"), "1",
                StringComparison.Ordinal);
            if (assertCurrentOptimizations && scenario == "held-prefix" && metrics.NativeSqliteTraceEnabled)
            {
                Assert.IsFalse(aggregate.SqlShapes.Any(shape => shape.Category == "operation"
                    && shape.Sql.TrimStart().StartsWith("SELECT COALESCE(@", StringComparison.OrdinalIgnoreCase)
                    && shape.Sql.Contains("HeldResourceSnapshot", StringComparison.OrdinalIgnoreCase)),
                    "Held-prefix reservation must batch selected-resource membership instead of issuing one scalar snapshot query per row.");
                Assert.AreEqual(MeasuredDispatchCycles, sent.Sum(),
                    "The optimized held-prefix fixture must preserve the same eligible dispatch output.");
                var worklistSeedShapes = aggregate.SqlShapes.Where(shape => shape.Category == "operation"
                    && shape.Sql.TrimStart().StartsWith("INSERT OR IGNORE INTO temp.HeldResourceWorklist", StringComparison.OrdinalIgnoreCase)).ToArray();
                Assert.IsTrue(worklistSeedShapes.Length > 0
                    && worklistSeedShapes.All(shape => shape.Sql.Contains("SELECT DISTINCT", StringComparison.OrdinalIgnoreCase)),
                    "Each live-source worklist seed must be distinct before the primary-key staging insert.");
                var worklistPageShapes = aggregate.SqlShapes.Where(shape => shape.Category == "operation"
                    && shape.Sql.TrimStart().StartsWith("SELECT ResourcePk FROM temp.HeldResourceWorklist", StringComparison.OrdinalIgnoreCase)).ToArray();
                Assert.AreEqual(1, worklistPageShapes.Length,
                    "Held worklist enumeration must use one stable keyset-page query shape.");
                StringAssert.Contains(worklistPageShapes[0].Sql, "LIMIT $limit");
                Assert.IsTrue(worklistPageShapes[0].Count >= MeasuredDispatchCycles,
                    "The held-prefix fixture must actually traverse staged pages.");
            }
            if (metrics.NativeSqliteProfileEnabled)
            {
                Assert.IsTrue(aggregate.SqlProfiles.Count > 0,
                    "Opt-in native SQLite profile must capture completed SQL statements.");
                Assert.IsTrue(aggregate.SqlProfiles.All(profile => profile.ExecutionCount > 0
                    && profile.TotalElapsedNanoseconds >= 0 && profile.MaxElapsedNanoseconds >= 0));
                TestContext.WriteLine("RC5_SQLITE_PROFILE_JSON:" + JsonSerializer.Serialize(new
                {
                    totalProfiledStatementNanoseconds = aggregate.SqlProfiles.Sum(profile => profile.TotalElapsedNanoseconds),
                    profileRows = aggregate.SqlProfiles.Take(20).ToArray(),
                }));
            }
            if (assertCurrentOptimizations && (scenario is "empty" or "capacity-full"))
                Assert.AreEqual(0L, operation.ImmediateWriterTransactions,
                    "No-expiry and capacity-full paths must not start a candidate/reservation writer transaction.");
            if (assertCurrentOptimizations && scenario == "capacity-full")
            {
                Assert.IsTrue(snapshots.All(x => !x.SqlShapes.Any(shape => shape.Category == "operation"
                    && shape.Sql.Contains("collection_task_outbox",
                    StringComparison.OrdinalIgnoreCase) && shape.Count > 0)),
                    "A full-capacity candidate path must skip pending/reservation SQL; telemetry is separately tagged.");
                Assert.IsTrue(resolverInputs.All(count => count == 0));
            }

            var plans = CaptureQueryPlans(sample, aggregate.SqlShapes);
            if (assertCurrentOptimizations && scenario == "held-prefix" && metrics.NativeSqliteTraceEnabled)
            {
                var worklistPagePlan = plans.Single(plan => plan.Sql.TrimStart().StartsWith(
                    "SELECT ResourcePk FROM temp.HeldResourceWorklist", StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(worklistPagePlan.PlanDetails.Any(detail => detail.Contains(
                    "SEARCH temp.HeldResourceWorklist USING INTEGER PRIMARY KEY", StringComparison.OrdinalIgnoreCase)),
                    "Held-resource pages must seek the TEMP worklist primary key rather than rescan or sort the staged set.");
            }
            var statusRecorderMode = GetStatusRecorderMode(dispatcher);
            if (assertCurrentOptimizations && !StatusRecorderControlOff)
                Assert.AreEqual("in-memory-recorder-on", statusRecorderMode,
                    "The current dispatcher measurement must include the production in-memory status recorder.");
            return new CollectionDispatchRc5ActionResult("dispatcher",
                "CollectionPlatformOutboxDispatcher.DispatchOnceAsync with queue, telemetry and in-memory status recorder when available",
                MeasuredDispatchCycles,
                Percentile(durations, 0.50), Percentile(durations, 0.95), PercentileLong(allocations, 0.50),
                PercentileLong(allocations, 0.95), sampler.PeakBytes, 0,
                fixture.OldestOutboxWaitSeconds, sent.Sum(), null, resolverInputs.Sum(), telemetry.SnapshotCount,
                statusRecorderMode, aggregate, telemetryMetrics, plans,
                DiagnosticSamples: diagnosticSamples);
        }
        finally { metrics.Dispose(); }
    }

    private async Task<CollectionDispatchRc5ActionResult> MeasureScheduleAsync(string template, string sample,
        string directory, string scenario, CollectionDispatchRc5FixtureManifest fixture,
        string fixtureVariant, string revision, bool directScheduleRequests)
    {
        var metrics = CreateMeasurementInstrumentation();
        try
        {
            RestoreDatabase(template, sample);
            var resolver = new CountingRaceIdentityResolver();
            var store = CreateStore(sample, metrics, resolver);
            var policy = new MeasurementSchedulePolicy();
            if (directScheduleRequests)
            {
                await ValidateDirectScheduleFixtureAsync(sample, store, resolver, metrics, scenario, fixtureVariant, fixture);
                await RunDirectScheduleRequestsAsync(store);
            }
            else
            {
                var warmup = new CollectionScheduleService(store, [policy],
                    NullLogger<CollectionScheduleService>.Instance);
                await warmup.RunOnceAsync(Now, CancellationToken.None);
            }
            RestoreDatabase(template, sample);

            using var sampler = new WorkingSetSampler();
            var durations = new List<double>(MeasuredProducerCycles);
            var allocations = new List<long>(MeasuredProducerCycles);
            var snapshots = new List<CollectionDispatchRc5MetricsSnapshot>(MeasuredProducerCycles);
            var createdTasks = 0;
            var resolverInputs = 0;
            var diagnosticSamples = CaptureDiagnosticSamples
                ? new List<CollectionDispatchRc5DiagnosticSample>(MeasuredProducerCycles) : null;
            var recorderMode = directScheduleRequests ? "control-off-direct-request-diagnostic" : "not-wired-adapter";
            for (var index = 0; index < MeasuredProducerCycles; index++)
            {
                RestoreDatabase(template, sample);
                resolver.Reset();
                metrics.Reset();
                CollectionScheduleService? service = null;
                if (!directScheduleRequests)
                {
                    var createdService = CreateScheduleService(store, policy);
                    service = createdService.Service;
                    recorderMode = createdService.RecorderMode;
                    if (string.Equals(Environment.GetEnvironmentVariable("ASSERT_COLLECTION_RC5_OPTIMIZATIONS"), "1",
                            StringComparison.Ordinal) && !StatusRecorderControlOff)
                        Assert.AreEqual("in-memory-recorder-on", createdService.RecorderMode,
                            "The current schedule producer measurement must include the production in-memory status recorder.");
                }
                var beforeTasks = CountRows(sample, "collection_tasks");
                var beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
                var stopwatch = Stopwatch.StartNew();
                using (metrics.BeginMeasurementWindow())
                using (sampler.MeasurementWindow())
                {
                    if (directScheduleRequests)
                        await RunDirectScheduleRequestsAsync(store);
                    else
                        await service!.RunOnceAsync(Now, CancellationToken.None);
                }
                stopwatch.Stop();
                var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated;
                allocations.Add(allocatedBytes);
                durations.Add(stopwatch.Elapsed.TotalMilliseconds);
                var snapshot = metrics.Snapshot();
                snapshots.Add(snapshot);
                AssertMeasurementObserver(snapshot, metrics, "schedule-producer", index);
                var cycleCreatedTasks = CountRows(sample, "collection_tasks") - beforeTasks;
                var cycleResolverInputs = resolver.Calls;
                Assert.AreEqual(ExpectedScheduleTasksPerCycle(scenario, fixtureVariant), cycleCreatedTasks,
                    $"Unexpected scheduled-task output in {scenario}, cycle {index}, fixture {fixtureVariant}, revision {revision}.");
                resolverInputs += cycleResolverInputs;
                createdTasks += cycleCreatedTasks;
                if (diagnosticSamples is not null)
                    diagnosticSamples.Add(CreateDiagnosticSample(index, stopwatch.Elapsed.TotalMilliseconds,
                        allocatedBytes, null, cycleCreatedTasks, cycleResolverInputs, null, snapshot, metrics));
            }

            var aggregate = Aggregate(snapshots);
            return new CollectionDispatchRc5ActionResult("schedule-producer",
                directScheduleRequests
                    ? "DIAGNOSTIC ONLY: 20 public RequestAsync calls on prevalidated eligible fixture; bypasses producer candidate paging/freshness/active-task/initial hold checks; RequestCore hold guard retained"
                    : "CollectionScheduleService.RunOnceAsync adapter; runtime recorder included when available",
                MeasuredProducerCycles,
                Percentile(durations, 0.50), Percentile(durations, 0.95), PercentileLong(allocations, 0.50),
                PercentileLong(allocations, 0.95), sampler.PeakBytes, 0,
                fixture.OldestScheduleWaitSeconds, 0, createdTasks, resolverInputs, 0,
                recorderMode, aggregate, CollectionDispatchRc5MetricCounts.Empty,
                CaptureQueryPlans(sample, aggregate.SqlShapes), DiagnosticSamples: diagnosticSamples);
        }
        finally { metrics.Dispose(); }
    }

    private async Task<CollectionDispatchRc5ActionResult> MeasureRecoveryAsync(string template, string sample,
        string directory, string scenario, CollectionDispatchRc5FixtureManifest fixture)
    {
        var metrics = CreateMeasurementInstrumentation();
        try
        {
            RestoreDatabase(template, sample);
            var store = CreateStore(sample, metrics, new CountingRaceIdentityResolver());
            await store.ResumeIncompleteBackfillBatchesAsync(Now, CancellationToken.None);
            RestoreDatabase(template, sample);

            using var sampler = new WorkingSetSampler();
            var durations = new List<double>(MeasuredProducerCycles);
            var allocations = new List<long>(MeasuredProducerCycles);
            var snapshots = new List<CollectionDispatchRc5MetricsSnapshot>(MeasuredProducerCycles);
            var createdTasks = 0;
            var visitedBatches = 0;
            var diagnosticSamples = CaptureDiagnosticSamples
                ? new List<CollectionDispatchRc5DiagnosticSample>(MeasuredProducerCycles) : null;
            for (var index = 0; index < MeasuredProducerCycles; index++)
            {
                RestoreDatabase(template, sample);
                metrics.Reset();
                var beforeTasks = CountRows(sample, "collection_tasks");
                var beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
                var stopwatch = Stopwatch.StartNew();
                int visited;
                using (metrics.BeginMeasurementWindow())
                using (sampler.MeasurementWindow())
                    visited = await store.ResumeIncompleteBackfillBatchesAsync(Now, CancellationToken.None);
                stopwatch.Stop();
                var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated;
                allocations.Add(allocatedBytes);
                durations.Add(stopwatch.Elapsed.TotalMilliseconds);
                var snapshot = metrics.Snapshot();
                snapshots.Add(snapshot);
                AssertMeasurementObserver(snapshot, metrics, "backfill-recovery", index);
                var cycleCreatedTasks = CountRows(sample, "collection_tasks") - beforeTasks;
                Assert.AreEqual(ExpectedRecoveryTasksPerCycle(scenario), cycleCreatedTasks,
                    $"Unexpected recovery-task output in {scenario}, cycle {index}.");
                createdTasks += cycleCreatedTasks;
                visitedBatches += visited;
                if (diagnosticSamples is not null)
                    diagnosticSamples.Add(CreateDiagnosticSample(index, stopwatch.Elapsed.TotalMilliseconds,
                        allocatedBytes, null, cycleCreatedTasks, 0, visited, snapshot, metrics));
            }

            var aggregate = Aggregate(snapshots);
            return new CollectionDispatchRc5ActionResult("backfill-recovery",
                "Store.ResumeIncompleteBackfillBatchesAsync direct adapter; hosted recorder excluded",
                MeasuredProducerCycles,
                Percentile(durations, 0.50), Percentile(durations, 0.95), PercentileLong(allocations, 0.50),
                PercentileLong(allocations, 0.95), sampler.PeakBytes, 0, null,
                0, createdTasks, 0, 0, "not-wired-store-adapter", aggregate,
                CollectionDispatchRc5MetricCounts.Empty, CaptureQueryPlans(sample, aggregate.SqlShapes),
                visitedBatches, diagnosticSamples);
        }
        finally { metrics.Dispose(); }
    }

    private static async Task<CollectionDispatchRc5FixtureManifest> CreateFixtureAsync(string path, string scenario)
    {
        var options = BuildDbOptions(BuildConnectionString(path));
        _ = new CollectionPlatformStore(options);
        var historyFactor = scenario == "fixed-live-history-10x" ? 10 : 1;
        var dispatchCount = scenario switch
        {
            "empty" => 0,
            "capacity-full" => 16,
            "held-prefix" => 520,
            "all-lanes-ready" => 30,
            "fixed-live-history-1x" or "fixed-live-history-10x" => 24,
            _ => 20,
        };
        var heldDispatchCount = scenario switch
        {
            "held-prefix" => 512,
            "fixed-live-history-1x" or "fixed-live-history-10x" => 1,
            _ => 0,
        };
        var scheduleCount = scenario switch
        {
            "empty" => 0,
            "dense-active-prefix" => 32,
            "held-prefix" => 32,
            "fixed-live-history-1x" or "fixed-live-history-10x" => 24,
            _ => 20,
        };
        var activeSchedulePrefix = scenario == "dense-active-prefix" ? 512 : 0;
        var heldSchedulePrefix = scenario == "held-prefix" ? 512 : 0;
        var historyCount = scenario == "empty" ? 0 : 64 * historyFactor;
        var manifestRows = new List<string>();

        await using (var db = new CollectionPlatformDbContext(options))
        {
            db.Definitions.AddRange(
                new CollectionDefinitionEntity
                {
                    DefinitionId = DispatchDefinition.Value,
                    Name = "RC5 dispatch",
                    ResourceType = CollectionResourceType.Race,
                    CurrentRevision = 1,
                    Enabled = true,
                },
                new CollectionDefinitionEntity
                {
                    DefinitionId = ScheduleDefinition.Value,
                    Name = "RC5 schedule",
                    ResourceType = CollectionResourceType.Race,
                    CurrentRevision = 1,
                    Enabled = true,
                },
                new CollectionDefinitionEntity
                {
                    DefinitionId = "race-discovery",
                    Name = "RC5 recovery",
                    ResourceType = CollectionResourceType.Race,
                    CurrentRevision = 1,
                    Enabled = true,
                });
            db.Revisions.AddRange(new[] { DispatchDefinition.Value, ScheduleDefinition.Value, "race-discovery" }
                .Select(definition => new CollectionRevisionEntity
                {
                    DefinitionId = definition,
                    Revision = 1,
                    Description = "RC5 deterministic fixture",
                    CreatedAt = Now,
                }));
            await db.SaveChangesAsync();

            var nextResourcePk = 1L;
            var resources = new List<CollectionResourceEntity>();
            var requests = new List<CollectionRequestEntity>();
            var tasks = new List<CollectionTaskEntity>();
            var activeTasks = new List<CollectionActiveTaskEntity>();
            var outboxes = new List<CollectionDispatchOutboxEntity>();
            var states = new List<CollectionStateEntity>();

            void AddDispatch(string id, CollectionLane lane, bool held)
            {
                var resourcePk = nextResourcePk++;
                var resourceId = held ? $"held-{id}" : id;
                resources.Add(new CollectionResourceEntity
                {
                    ResourcePk = resourcePk,
                    Type = CollectionResourceType.Race,
                    Provider = "JRA",
                    ResourceId = resourceId,
                    AttributesJson = "{}",
                    EffectiveDate = new DateOnly(2026, 10, 7),
                    CreatedAt = Now.AddMinutes(-2),
                });
                var requestId = StableGuid("request:" + id);
                var taskId = StableGuid("task:" + id);
                var outboxId = StableGuid("outbox:" + id);
                requests.Add(new CollectionRequestEntity
                {
                    RequestId = requestId,
                    ResourcePk = resourcePk,
                    DefinitionId = DispatchDefinition.Value,
                    RequestedRevision = 1,
                    Reason = CollectionReason.Initial,
                    Lane = lane,
                    Priority = lane == CollectionLane.Realtime ? 100 : lane == CollectionLane.Normal ? 50 : 10,
                    RequestedAt = Now.AddMinutes(-1),
                    MetadataJson = "{}",
                });
                tasks.Add(new CollectionTaskEntity
                {
                    TaskId = taskId,
                    RequestId = requestId,
                    ResourcePk = resourcePk,
                    DefinitionId = DispatchDefinition.Value,
                    RequestedRevision = 1,
                    Status = CollectionTaskStatus.Ready,
                    Lane = lane,
                    Priority = lane == CollectionLane.Realtime ? 100 : lane == CollectionLane.Normal ? 50 : 10,
                    AvailableAt = Now.AddSeconds(-30),
                    CreatedAt = Now.AddMinutes(-1),
                    UpdatedAt = Now.AddMinutes(-1),
                    DispatchGeneration = 1,
                });
                activeTasks.Add(new CollectionActiveTaskEntity
                {
                    ResourcePk = resourcePk,
                    DefinitionId = DispatchDefinition.Value,
                    TaskId = taskId,
                });
                outboxes.Add(new CollectionDispatchOutboxEntity
                {
                    OutboxId = outboxId,
                    TaskId = taskId,
                    DispatchGeneration = 1,
                    AvailableAt = Now.AddSeconds(-30),
                    CreatedAt = Now.AddMinutes(-1),
                });
                manifestRows.Add($"dispatch|{resourcePk}|{resourceId}|{lane}|{requestId:N}|{taskId:N}|{outboxId:N}");
            }

            for (var index = 0; index < dispatchCount; index++)
            {
                var lane = scenario == "all-lanes-ready" ? (CollectionLane)(index % 3) : CollectionLane.Normal;
                AddDispatch($"dispatch-{index:D4}", lane, index < heldDispatchCount);
            }

            for (var index = 0; index < activeSchedulePrefix + heldSchedulePrefix + scheduleCount; index++)
            {
                var held = index >= activeSchedulePrefix && index < activeSchedulePrefix + heldSchedulePrefix;
                var active = index < activeSchedulePrefix;
                var resourcePk = nextResourcePk++;
                var resourceId = held ? $"held-schedule-{index:D4}" : $"schedule-{index:D4}";
                resources.Add(new CollectionResourceEntity
                {
                    ResourcePk = resourcePk,
                    Type = CollectionResourceType.Race,
                    Provider = "JRA",
                    ResourceId = resourceId,
                    AttributesJson = "{}",
                    EffectiveDate = new DateOnly(2026, 10, 7),
                    CreatedAt = Now.AddMinutes(-1),
                });
                states.Add(new CollectionStateEntity
                {
                    ResourcePk = resourcePk,
                    DefinitionId = ScheduleDefinition.Value,
                    AppliedRevision = 1,
                    RequiredRevision = 1,
                    LastCollectedAt = Now.AddDays(-1),
                    NextCollectionAt = Now.AddSeconds(-30),
                    Status = CollectionStateStatus.RefreshDue,
                    UpdatedAt = Now.AddMinutes(-1),
                });
                manifestRows.Add($"state|{resourcePk}|{resourceId}|{active}|{held}|{Now.AddSeconds(-30):O}");
                if (active)
                {
                    var requestId = StableGuid("active-request:" + index);
                    var taskId = StableGuid("active-task:" + index);
                    requests.Add(new CollectionRequestEntity
                    {
                        RequestId = requestId,
                        ResourcePk = resourcePk,
                        DefinitionId = ScheduleDefinition.Value,
                        RequestedRevision = 1,
                        Reason = CollectionReason.Initial,
                        RequestedAt = Now.AddMinutes(-1),
                        MetadataJson = "{}",
                    });
                    tasks.Add(new CollectionTaskEntity
                    {
                        TaskId = taskId,
                        RequestId = requestId,
                        ResourcePk = resourcePk,
                        DefinitionId = ScheduleDefinition.Value,
                        RequestedRevision = 1,
                        Status = CollectionTaskStatus.Running,
                        Lane = CollectionLane.Normal,
                        Priority = 50,
                        AvailableAt = Now.AddMinutes(-1),
                        CreatedAt = Now.AddMinutes(-1),
                        UpdatedAt = Now.AddMinutes(-1),
                    });
                    activeTasks.Add(new CollectionActiveTaskEntity
                    {
                        ResourcePk = resourcePk,
                        DefinitionId = ScheduleDefinition.Value,
                        TaskId = taskId,
                    });
                }
            }

            for (var index = 0; index < historyCount; index++)
            {
                var resourcePk = nextResourcePk++;
                var resourceId = $"history-{index:D5}";
                var requestId = StableGuid("history-request:" + index);
                var taskId = StableGuid("history-task:" + index);
                resources.Add(new CollectionResourceEntity
                {
                    ResourcePk = resourcePk,
                    Type = CollectionResourceType.Race,
                    Provider = "JRA",
                    ResourceId = resourceId,
                    AttributesJson = "{}",
                    CreatedAt = Now.AddDays(-365),
                });
                requests.Add(new CollectionRequestEntity
                {
                    RequestId = requestId,
                    ResourcePk = resourcePk,
                    DefinitionId = DispatchDefinition.Value,
                    RequestedRevision = 1,
                    Reason = CollectionReason.Initial,
                    RequestedAt = Now.AddDays(-365),
                    MetadataJson = "{}",
                });
                tasks.Add(new CollectionTaskEntity
                {
                    TaskId = taskId,
                    RequestId = requestId,
                    ResourcePk = resourcePk,
                    DefinitionId = DispatchDefinition.Value,
                    RequestedRevision = 1,
                    Status = CollectionTaskStatus.Succeeded,
                    Lane = CollectionLane.Normal,
                    Priority = 50,
                    AvailableAt = Now.AddDays(-365),
                    CreatedAt = Now.AddDays(-365),
                    UpdatedAt = Now.AddDays(-365),
                    FinishedAt = Now.AddDays(-364),
                });
                manifestRows.Add($"history|{resourcePk}|{resourceId}|{requestId:N}|{taskId:N}");
            }

            if (scenario != "empty")
            {
                resources.Add(new CollectionResourceEntity
                {
                    ResourcePk = nextResourcePk,
                    Type = CollectionResourceType.Race,
                    Provider = "JRA",
                    ResourceId = "backfill:20260101",
                    EffectiveDate = new DateOnly(2026, 1, 1),
                    AttributesJson = "{}",
                    CreatedAt = Now.AddDays(-10),
                });
                requests.Add(new CollectionRequestEntity
                {
                    RequestId = StableGuid("legacy-backfill-request"),
                    ResourcePk = nextResourcePk,
                    DefinitionId = "race-discovery",
                    RequestedRevision = 1,
                    Reason = CollectionReason.Backfill,
                    Lane = CollectionLane.Background,
                    Priority = 10,
                    RequestedAt = Now.AddDays(-10),
                    BatchId = "rc5-legacy-backfill",
                    MetadataJson = "{\"batchId\":\"rc5-legacy-backfill\",\"backfillDate\":\"2026-01-01\"}",
                });
                db.BackfillBatches.Add(new BackfillBatchEntity
                {
                    BatchId = "rc5-legacy-backfill",
                    Provider = "JRA",
                    From = new DateOnly(2026, 1, 1),
                    To = new DateOnly(2026, 1, 7),
                    CreatedAt = Now.AddDays(-10),
                });
            }
            if (heldDispatchCount + heldSchedulePrefix > 0)
                db.RaceRepairHolds.Add(new RaceRepairHoldEntity
                {
                    RaceId = "race-held",
                    Generation = 1,
                    OperationId = "rc5-hold",
                    Reason = "deterministic RC5 fixture",
                    CreatedAt = Now.AddMinutes(-1),
                });
            if (scenario == "capacity-full")
                db.ExecutionLeases.Add(new CollectionExecutionLeaseEntity
                {
                    ExecutionBatchId = StableGuid("full-execution-batch"),
                    DispatchEnvelopeId = StableGuid("full-envelope"),
                    WakeId = StableGuid("full-wake"),
                    ReservationToken = "rc5-capacity-reservation",
                    LeaseToken = "rc5-capacity-lease",
                    Status = "Running",
                    LeaseExpiresAt = Now.AddHours(1),
                    CreatedAt = Now.AddMinutes(-1),
                    StartedAt = Now.AddMinutes(-1),
                });

            db.Resources.AddRange(resources);
            db.Requests.AddRange(requests);
            db.Tasks.AddRange(tasks);
            db.ActiveTasks.AddRange(activeTasks);
            db.DispatchOutbox.AddRange(outboxes);
            db.States.AddRange(states);
            await db.SaveChangesAsync();
        }

        var businessRows = string.Join('\n', manifestRows.Order(StringComparer.Ordinal));
        var manifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(businessRows)));
        return new CollectionDispatchRc5FixtureManifest(scenario, manifestHash,
            manifestRows.Count(x => x.StartsWith("dispatch|", StringComparison.Ordinal)),
            manifestRows.Count(x => x.StartsWith("state|", StringComparison.Ordinal)),
            historyCount, dispatchCount, heldDispatchCount, activeSchedulePrefix, heldSchedulePrefix,
            Now - Now.AddSeconds(-30), Now - Now.AddSeconds(-30),
            "v2: held-* => race-held; all other candidate IDs => deterministic race-{SHA256-derived GUID}; no unknown race aliases",
            "Canonical business fixture rows only; migration history and current-only recovery sidecars excluded.");
    }

    private static CollectionPlatformStore CreateStore(string path,
        CollectionDispatchRc5MeasurementInstrumentation instrumentation, CountingRaceIdentityResolver resolver)
    {
        var store = new CollectionPlatformStore(BuildDbOptions(BuildConnectionString(path), instrumentation));
        typeof(CollectionPlatformStore).GetField("_raceIdentityResolver", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(store, resolver);
        return store;
    }

    private static CollectionDispatchRc5MeasurementInstrumentation CreateMeasurementInstrumentation()
        => new(CollectionDispatchRc5MeasurementInstrumentation.NativeTraceEnabledFromEnvironment,
            CollectionDispatchRc5MeasurementInstrumentation.NativeProfileEnabledFromEnvironment);

    private static int ParseMeasuredProducerCycles(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultMeasuredProducerCycles;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var cycles))
            throw new ArgumentException("RC5_PRODUCER_CYCLES must be an integer from 10 through 200.", nameof(value));
        if (cycles is < DefaultMeasuredProducerCycles or > MaximumDiagnosticProducerCycles)
            throw new ArgumentOutOfRangeException(nameof(value), cycles,
                "RC5_PRODUCER_CYCLES must be from 10 through 200.");
        return cycles;
    }

    private static string ParseRevisionLabel(string? value)
        => string.IsNullOrWhiteSpace(value) ? "not-supplied" : value;

    private static bool ParseDirectScheduleRequestControl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
        throw new ArgumentException("RC5_SCHEDULE_DIRECT_REQUESTS must be true or false.", nameof(value));
    }

    private static void ValidateDirectScheduleDiagnostic(bool enabled, string scenario, string fixtureVariant)
    {
        if (!enabled) return;
        if (scenario != "all-lanes-ready" || fixtureVariant != "current")
            throw new InvalidOperationException(
                "The direct RequestAsync diagnostic is restricted to the current AllLanesReady fixture.");
    }

    private static void ValidateDirectScheduleFixtureManifest(CollectionDispatchRc5FixtureManifest fixture)
    {
        if (fixture.Scenario != "all-lanes-ready" || fixture.ScheduleStates != 20
            || fixture.ActiveSchedulePrefix != 0 || fixture.HeldSchedulePrefix != 0)
            throw new InvalidOperationException(
                "The direct RequestAsync diagnostic requires exactly 20 non-active, non-prefix schedule states.");
    }

    private static bool CandidateIsHeldByActiveRepairHolds(IEnumerable<string?> candidateRaceIds,
        IReadOnlyCollection<string> activeHoldRaceIds)
        => candidateRaceIds.Any(raceId => raceId is null
            ? activeHoldRaceIds.Count > 0
            : activeHoldRaceIds.Contains(raceId, StringComparer.Ordinal));

    private static async Task ValidateDirectScheduleFixtureAsync(string sample, CollectionPlatformStore store,
        CountingRaceIdentityResolver resolver, CollectionDispatchRc5MeasurementInstrumentation metrics,
        string scenario, string fixtureVariant, CollectionDispatchRc5FixtureManifest fixture)
    {
        ValidateDirectScheduleDiagnostic(true, scenario, fixtureVariant);
        ValidateDirectScheduleFixtureManifest(fixture);

        var candidates = await store.GetDueScheduleCandidatesAsync(Now, limit: 21).ConfigureAwait(false);
        var expectedIds = Enumerable.Range(0, 20).Select(index => $"schedule-{index:D4}")
            .Order(StringComparer.Ordinal).ToArray();
        var actualIds = candidates.Select(candidate => candidate.State.Resource.Id)
            .Order(StringComparer.Ordinal).ToArray();
        if (!actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal)
            || candidates.Any(candidate => candidate.HasActiveTask
                || candidate.State.Status is CollectionStateStatus.Collecting or CollectionStateStatus.Failed
                || candidate.State.NextCollectionAt is not { } dueAt || dueAt > Now))
            throw new InvalidOperationException(
                "The direct RequestAsync diagnostic fixture no longer contains exactly 20 eligible due resources.");

        var activeHoldRaceIds = await ReadActiveRepairHoldRaceIdsAsync(sample, metrics).ConfigureAwait(false);
        var candidateRaceIds = candidates.Select(candidate => resolver.Resolve(candidate.State.Resource.Id,
            new Dictionary<string, string>())).ToArray();
        if (CandidateIsHeldByActiveRepairHolds(candidateRaceIds, activeHoldRaceIds))
            throw new InvalidOperationException(
                "At least one direct RequestAsync candidate is repair-held; unrelated active holds are permitted.");
        resolver.Reset();
        metrics.Reset();
    }

    private static async Task<string[]> ReadActiveRepairHoldRaceIdsAsync(string sample,
        CollectionDispatchRc5MeasurementInstrumentation metrics)
    {
        await using var db = new CollectionPlatformDbContext(BuildDbOptions(BuildConnectionString(sample), metrics));
        return await db.RaceRepairHolds.AsNoTracking().Where(hold => hold.ReleasedAt == null)
            .Select(hold => hold.RaceId).ToArrayAsync().ConfigureAwait(false);
    }

    private static async Task RunDirectScheduleRequestsAsync(CollectionPlatformStore store)
    {
        var created = 0;
        for (var index = 0; index < 20; index++)
        {
            var receipt = await store.RequestAsync(
                new(CollectionResourceType.Race, "JRA", $"schedule-{index:D4}"),
                ScheduleDefinition, 1, CollectionReason.ScheduledRefresh, Now,
                CollectionLane.Normal, (int)CollectionPriority.Normal).ConfigureAwait(false);
            Assert.IsTrue(receipt.CreatedTask,
                $"Direct RequestAsync did not create a new task for known-eligible schedule-{index:D4}.");
            created += receipt.CreatedTask ? 1 : 0;
        }
        Assert.AreEqual(20, created, "Direct RequestAsync must create exactly the frozen 20 task requests.");
    }

    private static string ParseFixtureVariant(string? value)
    {
        if (string.Equals(value, "baseline", StringComparison.OrdinalIgnoreCase)) return "baseline";
        if (string.Equals(value, "current", StringComparison.OrdinalIgnoreCase)) return "current";
        throw new ArgumentException("RC5_FIXTURE_VARIANT must be explicitly set to baseline or current.", nameof(value));
    }

    private static int ExpectedDispatchSendsPerCycle(string scenario)
        => scenario is "empty" or "capacity-full" ? 0 : 1;

    private static int ExpectedScheduleTasksPerCycle(string scenario, string fixtureVariant) => scenario switch
    {
        "empty" or "held-prefix" => 0,
        "dense-active-prefix" when string.Equals(fixtureVariant, "baseline", StringComparison.OrdinalIgnoreCase) => 0,
        "dense-active-prefix" => 32,
        "fixed-live-history-1x" or "fixed-live-history-10x" => 24,
        _ => 20,
    };

    private static int ExpectedRecoveryTasksPerCycle(string scenario)
        => scenario == "empty" ? 0 : 7;

    private static void AssertMeasurementObserver(CollectionDispatchRc5MetricsSnapshot snapshot,
        CollectionDispatchRc5MeasurementInstrumentation metrics, string action, int cycle)
    {
        Assert.AreEqual(0L, snapshot.NativeObserverErrors,
            $"The native SQLite observer must not drop callback errors in {action}, cycle {cycle}.");
        if (metrics.NativeSqliteTraceEnabled)
            Assert.IsTrue(snapshot.Total.SqlStatements > 0,
                $"The native SQLite observer must capture statements in {action}, cycle {cycle}.");
        if (!metrics.NativeSqliteProfileEnabled) return;

        Assert.IsTrue(snapshot.SqlProfiles.Count > 0,
            $"Opted-in SQLite profiling must capture completed statements in {action}, cycle {cycle}.");
        Assert.IsTrue(snapshot.SqlProfiles.All(profile => profile.ExecutionCount > 0
            && profile.TotalElapsedNanoseconds >= 0 && profile.MaxElapsedNanoseconds >= 0));
    }

    private static CollectionDispatchRc5DiagnosticSample CreateDiagnosticSample(int cycle,
        double elapsedMilliseconds, long allocatedBytes, int? queueSends, int? tasksCreated,
        int resolverInputs, int? batchesVisited, CollectionDispatchRc5MetricsSnapshot snapshot,
        CollectionDispatchRc5MeasurementInstrumentation metrics)
    {
        var profiles = metrics.NativeSqliteProfileEnabled
            ? snapshot.SqlProfiles.OrderByDescending(profile => profile.TotalElapsedNanoseconds)
                .Take(3).ToArray()
            : null;
        return new(cycle, elapsedMilliseconds, allocatedBytes, queueSends, tasksCreated,
            resolverInputs, batchesVisited,
            metrics.NativeSqliteTraceEnabled ? snapshot.Total.SqlStatements : null,
            metrics.NativeSqliteTraceEnabled ? snapshot.Total.SqliteRowsReturned : null,
            metrics.NativeSqliteProfileEnabled
                ? snapshot.SqlProfiles.Sum(profile => profile.TotalElapsedNanoseconds) : null,
            profiles);
    }

    private static CollectionPlatformOutboxDispatcher CreateDispatcher(CollectionPlatformStore store,
        MeasurementQueue queue, IOptions<CollectionQueueOptions> options, MeasurementTelemetry telemetry)
    {
        var type = typeof(CollectionPlatformOutboxDispatcher);
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var recorder = StatusRecorderControlOff ? null : CreateRuntimeStatusRecorder(type.Assembly);
        var sixArgument = constructors.FirstOrDefault(ctor => ctor.GetParameters().Length == 6);
        if (sixArgument is not null && recorder is not null)
            return (CollectionPlatformOutboxDispatcher)sixArgument.Invoke(
                [store, queue, options, NullLogger<CollectionPlatformOutboxDispatcher>.Instance, telemetry, recorder]);
        var fiveArgument = constructors.First(ctor => ctor.GetParameters().Length == 5);
        return (CollectionPlatformOutboxDispatcher)fiveArgument.Invoke(
            [store, queue, options, NullLogger<CollectionPlatformOutboxDispatcher>.Instance, telemetry]);
    }

    private static (CollectionScheduleService Service, string RecorderMode) CreateScheduleService(
        CollectionPlatformStore store, ICollectionSchedulePolicy policy)
    {
        var type = typeof(CollectionScheduleService);
        var recorder = StatusRecorderControlOff ? null : CreateRuntimeStatusRecorder(type.Assembly);
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var fourArgument = constructors.FirstOrDefault(ctor => ctor.GetParameters().Length == 4);
        if (fourArgument is not null && recorder is not null)
            return ((CollectionScheduleService)fourArgument.Invoke(
                [store, new[] { policy }, NullLogger<CollectionScheduleService>.Instance, recorder]),
                "in-memory-recorder-on");
        var threeArgument = constructors.First(ctor => ctor.GetParameters().Length == 3);
        return ((CollectionScheduleService)threeArgument.Invoke(
            [store, new[] { policy }, NullLogger<CollectionScheduleService>.Instance]),
            StatusRecorderControlOff ? "control-off" : "not-wired-adapter");
    }

    private static object? CreateRuntimeStatusRecorder(Assembly apiAssembly)
    {
        var recorderType = apiAssembly.GetType("HorseRacingPrediction.Api.CollectionController.CollectionRuntimeStatusRecorder");
        var configurationType = apiAssembly.GetType("HorseRacingPrediction.Api.CollectionController.CollectionRuntimeActionConfiguration");
        var actionType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("HorseRacingPrediction.Contracts.Collection.CollectionRuntimeAction"))
            .FirstOrDefault(type => type is not null);
        if (recorderType is null || configurationType is null || actionType is null) return null;

        var actionValues = Enum.GetValues(actionType);
        var configurations = Array.CreateInstance(configurationType, actionValues.Length);
        var configurationConstructor = configurationType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance).First(ctor => ctor.GetParameters().Length == 3);
        for (var index = 0; index < actionValues.Length; index++)
            configurations.SetValue(configurationConstructor.Invoke([actionValues.GetValue(index), true, null]), index);
        var recorderConstructor = recorderType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance).First(ctor => ctor.GetParameters().Length == 2);
        return recorderConstructor.Invoke([configurations, TimeProvider.System]);
    }

    private static void SetCurrentTime(CollectionPlatformOutboxDispatcher dispatcher)
        => typeof(CollectionPlatformOutboxDispatcher).GetProperty("CurrentTime", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(dispatcher, new Func<DateTimeOffset>(() => Now));

    private static void SetDispatcherTelemetrySnapshotTime(CollectionPlatformOutboxDispatcher dispatcher, long ticks)
        => typeof(CollectionPlatformOutboxDispatcher).GetField("_lastTelemetrySnapshotUtcTicks",
            BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(dispatcher, ticks);

    private static string GetStatusRecorderMode(CollectionPlatformOutboxDispatcher dispatcher)
    {
        if (StatusRecorderControlOff) return "control-off";
        var recorderField = dispatcher.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(field => field.FieldType.FullName ==
                "HorseRacingPrediction.Api.CollectionController.CollectionRuntimeStatusRecorder");
        return recorderField?.GetValue(dispatcher) is null ? "off-or-not-available" : "in-memory-recorder-on";
    }

    private static DbContextOptions<CollectionPlatformDbContext> BuildDbOptions(string connectionString,
        CollectionDispatchRc5MeasurementInstrumentation? instrumentation = null)
    {
        var builder = new DbContextOptionsBuilder<CollectionPlatformDbContext>().UseSqlite(connectionString);
        if (instrumentation is not null) builder.AddInterceptors(instrumentation);
        return builder.Options;
    }

    private static string BuildConnectionString(string path, int timeoutSeconds = 30)
        => new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            DefaultTimeout = timeoutSeconds,
        }.ToString();

    private static async Task SeedDefinitionsAsync(DbContextOptions<CollectionPlatformDbContext> options)
    {
        await using var db = new CollectionPlatformDbContext(options);
        db.Definitions.Add(new CollectionDefinitionEntity
        {
            DefinitionId = DispatchDefinition.Value,
            Name = "RC5 lock",
            ResourceType = CollectionResourceType.Race,
            CurrentRevision = 1,
            Enabled = true,
        });
        db.Revisions.Add(new CollectionRevisionEntity
        {
            DefinitionId = DispatchDefinition.Value,
            Revision = 1,
            Description = "lock test",
            CreatedAt = Now,
        });
        await db.SaveChangesAsync();
    }

    private static void RestoreDatabase(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(BuildConnectionString(sourcePath));
        using var destination = new SqliteConnection(BuildConnectionString(destinationPath));
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static int CountRows(string path, string table)
    {
        using var connection = new SqliteConnection(BuildConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ReadSqliteVersion(string path)
    {
        using var connection = new SqliteConnection(BuildConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
    }

    private static IReadOnlyList<CollectionDispatchRc5QueryPlan> CaptureQueryPlans(string path,
        IReadOnlyList<CollectionDispatchRc5SqlShape> shapes)
    {
        using var connection = new SqliteConnection(BuildConnectionString(path));
        connection.Open();
        if (shapes.Any(shape => shape.Sql.Contains("HeldResourceWorklist", StringComparison.OrdinalIgnoreCase)))
        {
            using var createWorklist = connection.CreateCommand();
            createWorklist.CommandText = "CREATE TEMP TABLE HeldResourceWorklist (ResourcePk INTEGER PRIMARY KEY);";
            createWorklist.ExecuteNonQuery();
        }
        if (shapes.Any(shape => shape.Sql.Contains("HeldResourceSnapshot", StringComparison.OrdinalIgnoreCase)))
        {
            using var createSnapshot = connection.CreateCommand();
            createSnapshot.CommandText = "CREATE TEMP TABLE HeldResourceSnapshot (ResourcePk INTEGER PRIMARY KEY, IsHeld INTEGER NOT NULL);";
            createSnapshot.ExecuteNonQuery();
        }
        var plans = new List<CollectionDispatchRc5QueryPlan>();
        foreach (var shape in shapes.Where(shape => StartsWithSelect(shape.Sql)
                     && (shape.Sql.Contains("collection_task_outbox", StringComparison.OrdinalIgnoreCase)
                         || shape.Sql.Contains("collection_resources", StringComparison.OrdinalIgnoreCase)
                         || shape.Sql.Contains("HeldResourceWorklist", StringComparison.OrdinalIgnoreCase)
                         || shape.Sql.Contains("collection_states", StringComparison.OrdinalIgnoreCase)))
                 .Take(20))
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "EXPLAIN QUERY PLAN " + shape.Sql;
                using var reader = command.ExecuteReader();
                var details = new List<string>();
                while (reader.Read()) details.Add(reader.GetString(3));
                plans.Add(new(shape.Sql, details));
            }
            catch (SqliteException ex)
            {
                plans.Add(new(shape.Sql, [$"EXPLAIN failed: {ex.SqliteErrorCode}/{ex.SqliteExtendedErrorCode}"]));
            }
        }
        return plans;
    }

    private static bool StartsWithSelect(string sql) => sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
        || sql.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

    private static CollectionDispatchRc5MetricsSnapshot Aggregate(
        IReadOnlyList<CollectionDispatchRc5MetricsSnapshot> snapshots)
    {
        var total = CollectionDispatchRc5MetricCounts.Empty;
        long materialized = 0;
        long tracked = 0;
        long nativeObserverErrors = 0;
        var categories = new Dictionary<string, CollectionDispatchRc5MetricCounts>(StringComparer.Ordinal);
        var shapes = new Dictionary<(string Category, string Sql), long>();
        var profiles = new Dictionary<(string Category, string Sql), (long Count, long Total, long Max)>();
        foreach (var snapshot in snapshots)
        {
            total += snapshot.Total;
            materialized += snapshot.EntityMaterializations;
            tracked += snapshot.TrackedEntities;
            nativeObserverErrors += snapshot.NativeObserverErrors;
            foreach (var category in snapshot.Categories)
                categories[category.Key] = categories.GetValueOrDefault(category.Key,
                    CollectionDispatchRc5MetricCounts.Empty) + category.Value;
            foreach (var shape in snapshot.SqlShapes)
            {
                var key = (shape.Category, shape.Sql);
                shapes[key] = shapes.GetValueOrDefault(key) + shape.Count;
            }
            foreach (var profile in snapshot.SqlProfiles)
            {
                var key = (profile.Category, profile.Sql);
                var current = profiles.GetValueOrDefault(key);
                profiles[key] = (current.Count + profile.ExecutionCount,
                    current.Total + profile.TotalElapsedNanoseconds,
                    Math.Max(current.Max, profile.MaxElapsedNanoseconds));
            }
        }
        return new(total, categories, materialized, tracked,
            nativeObserverErrors,
            shapes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.Category, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Sql, StringComparer.Ordinal)
                .Select(pair => new CollectionDispatchRc5SqlShape(pair.Key.Category, pair.Key.Sql, pair.Value)).ToArray(),
            profiles.Select(pair => new CollectionDispatchRc5SqlProfile(pair.Key.Category, pair.Key.Sql,
                    pair.Value.Count, pair.Value.Total, pair.Value.Max))
                .OrderByDescending(profile => profile.TotalElapsedNanoseconds)
                .ThenBy(profile => profile.Category, StringComparer.Ordinal)
                .ThenBy(profile => profile.Sql, StringComparer.Ordinal).ToArray());
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var ordered = values.Order().ToArray();
        var rank = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length), 1, ordered.Length);
        return ordered[rank - 1];
    }

    private static long PercentileLong(IReadOnlyList<long> values, double percentile)
    {
        var ordered = values.Order().ToArray();
        var rank = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length), 1, ordered.Length);
        return ordered[rank - 1];
    }

    private static Guid StableGuid(string seed) => new(SHA256.HashData(Encoding.UTF8.GetBytes(seed))[..16]);

    private static string CreateDirectory(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "collection-rc5", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CountingRaceIdentityResolver : IRaceResourceIdentityResolver
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public void Reset() => Interlocked.Exchange(ref _calls, 0);
        public string? Resolve(string resourceId, IReadOnlyDictionary<string, string> attributes)
        {
            Interlocked.Increment(ref _calls);
            return resourceId.StartsWith("held-", StringComparison.Ordinal)
                ? "race-held"
                : $"race-{StableGuid("rc5-resolver:" + resourceId):D}";
        }
    }

    private sealed class MeasurementSchedulePolicy : ICollectionSchedulePolicy
    {
        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
            => new(true, null, CollectionPriority.Normal, CollectionLane.Normal, "RC5 fixed due fixture");
    }

    private sealed class MeasurementQueue : ICollectionPlatformTaskQueue
    {
        private int _sendCount;
        public int SendCount => Volatile.Read(ref _sendCount);
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            return Task.FromResult(new CollectionQueueSendReceipt("rc5-measurement"));
        }
        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            return Task.FromResult(new CollectionQueueSendReceipt("rc5-measurement-wake"));
        }
        public void Reset() => Interlocked.Exchange(ref _sendCount, 0);
    }

    private sealed class MeasurementTelemetry(CollectionDispatchRc5MeasurementInstrumentation metrics)
        : ICollectionDispatchTelemetry
    {
        private int _snapshotCount;
        public int SnapshotCount => Volatile.Read(ref _snapshotCount);
        public Task RecordDispatchCycleAsync(CollectionDispatchCycleOutcome outcome, CollectionLane? lane = null,
            string? definitionId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordAcquireAsync(CollectionExecutionAcquireStatus status,
            CollectionExecutionNoWorkReason? reason, CollectionLane? lane = null, string? definitionId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordReservationReleaseAsync(CollectionReservationReleaseOutcome outcome,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordLeaseReclaimedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordTerminalCompletionAsync(CollectionLane lane, string definitionId,
            CollectionTaskStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordTerminalCompletionLookupAsync(
            Func<CancellationToken, Task<CollectionDispatchTaskTelemetryState?>> lookup,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task QueueSnapshotAsync(
            Func<CancellationToken, Task<CollectionDispatchTelemetrySnapshot>> query,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _snapshotCount);
            using (metrics.InCategory("diagnostic-telemetry"))
                _ = await query(cancellationToken);
        }
    }
}

internal sealed record CollectionDispatchRc5FixtureManifest(
    string Scenario,
    string LogicalBusinessRowsSha256,
    int DispatchResources,
    int ScheduleStates,
    int HistoricalTasks,
    int PendingDispatches,
    int HeldPendingDispatches,
    int ActiveSchedulePrefix,
    int HeldSchedulePrefix,
    TimeSpan OldestOutboxWait,
    TimeSpan OldestScheduleWait,
    string ResolverIdentityRule,
    string Exclusions)
{
    public double? OldestOutboxWaitSeconds => PendingDispatches == 0 ? null : OldestOutboxWait.TotalSeconds;
    public double? OldestScheduleWaitSeconds => ScheduleStates == 0 ? null : OldestScheduleWait.TotalSeconds;
}

internal sealed record CollectionDispatchRc5ActionResult(
    string Action,
    string MeasurementScope,
    int Cycles,
    double P50Milliseconds,
    double P95Milliseconds,
    long AllocatedBytesP50,
    long AllocatedBytesP95,
    long PeakWorkingSetBytes,
    double RawLockWaitMilliseconds,
    double? OldestDueWaitSeconds,
    int QueueSends,
    int? TasksCreated,
    int ResolverInputs,
    int DiagnosticTelemetrySnapshots,
    string StatusRecorderMode,
    CollectionDispatchRc5MetricsSnapshot? Metrics,
    CollectionDispatchRc5MetricCounts? DiagnosticTelemetryMetrics,
    IReadOnlyList<CollectionDispatchRc5QueryPlan>? QueryPlans,
    int? LegacyOrVisitedBatches = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CollectionDispatchRc5DiagnosticSample>? DiagnosticSamples = null);

internal sealed record CollectionDispatchRc5DiagnosticSample(
    int Cycle,
    double ElapsedMilliseconds,
    long AllocatedBytes,
    int? QueueSends,
    int? TasksCreated,
    int ResolverInputs,
    int? BatchesVisited,
    long? SqlStatements,
    long? SqliteRowsReturned,
    long? SqlProfileTotalNanoseconds,
    IReadOnlyList<CollectionDispatchRc5SqlProfile>? TopSqlProfiles);

internal sealed record CollectionDispatchRc5QueryPlan(string Sql, IReadOnlyList<string> PlanDetails);

internal sealed class WorkingSetSampler : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _samplingTask;
    private int _active;
    private long _peakBytes;

    public WorkingSetSampler() => _samplingTask = Task.Run(SampleAsync);
    public long PeakBytes => Interlocked.Read(ref _peakBytes);

    public IDisposable MeasurementWindow()
    {
        Sample();
        Interlocked.Exchange(ref _active, 1);
        return new SampleScope(this);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _samplingTask.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
    }

    private async Task SampleAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            if (Volatile.Read(ref _active) != 0) Sample();
            await Task.Delay(5, _stop.Token).ConfigureAwait(false);
        }
    }

    private void Sample()
    {
        var bytes = Process.GetCurrentProcess().WorkingSet64;
        long observed;
        do
        {
            observed = Interlocked.Read(ref _peakBytes);
            if (bytes <= observed) return;
        } while (Interlocked.CompareExchange(ref _peakBytes, bytes, observed) != observed);
    }

    private sealed class SampleScope(WorkingSetSampler owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Sample();
                Interlocked.Exchange(ref owner._active, 0);
            }
        }
    }
}

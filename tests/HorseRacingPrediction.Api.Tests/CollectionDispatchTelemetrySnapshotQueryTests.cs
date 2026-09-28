using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using System.Data.Common;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionDispatchTelemetrySnapshotQueryTests
{
    [TestMethod]
    public async Task HighCardinalityBacklog_IsAggregatedBySqlIntoBoundedLaneResult()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dispatch-snapshot-high-cardinality", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var capture = new SqlCaptureInterceptor();
            var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False")
                .AddInterceptors(capture).Options;
            var store = new CollectionPlatformStore(options);
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow;
            await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "seed-resource"), new("race-detail"), 1,
                CollectionReason.Initial, now.AddMinutes(-1), lane: CollectionLane.Background);
            capture.Clear();

            const int extraRows = 2_000;
            await using (var db = new CollectionPlatformDbContext(options))
            {
                var resourcePk = await db.Resources.Where(resource => resource.ResourceId == "seed-resource")
                    .Select(resource => resource.ResourcePk).SingleAsync();
                var requests = new List<CollectionRequestEntity>(extraRows);
                var tasks = new List<CollectionTaskEntity>(extraRows);
                var outboxes = new List<CollectionDispatchOutboxEntity>(extraRows);
                for (var index = 0; index < extraRows; index++)
                {
                    var requestId = Guid.NewGuid();
                    var taskId = Guid.NewGuid();
                    requests.Add(new CollectionRequestEntity
                    {
                        RequestId = requestId,
                        ResourcePk = resourcePk,
                        DefinitionId = "race-detail",
                        RequestedRevision = 1,
                        Reason = CollectionReason.Initial,
                        Lane = CollectionLane.Background,
                        RequestedAt = now.AddMinutes(-1),
                    });
                    tasks.Add(new CollectionTaskEntity
                    {
                        TaskId = taskId,
                        RequestId = requestId,
                        ResourcePk = resourcePk,
                        DefinitionId = "race-detail",
                        RequestedRevision = 1,
                        Status = CollectionTaskStatus.Ready,
                        Lane = CollectionLane.Background,
                        AvailableAt = now.AddMinutes(-1),
                        CreatedAt = now.AddMinutes(-1),
                        UpdatedAt = now.AddMinutes(-1),
                    });
                    outboxes.Add(new CollectionDispatchOutboxEntity
                    {
                        OutboxId = Guid.NewGuid(),
                        TaskId = taskId,
                        AvailableAt = now.AddMinutes(-1),
                        CreatedAt = now.AddMinutes(-1),
                    });
                }
                db.Requests.AddRange(requests);
                db.Tasks.AddRange(tasks);
                db.DispatchOutbox.AddRange(outboxes);
                await db.SaveChangesAsync();
            }

            var snapshot = await store.GetDispatchTelemetrySnapshotAsync(now, 4, ["race-detail"], 0);

            Assert.AreEqual(1, snapshot.Lanes.Count, "Two thousand due rows should materialize as one aggregate row.");
            Assert.AreEqual(extraRows + 1, snapshot.Lanes.Single().EligibleReadyRows);
            var aggregateSql = capture.Commands.Single(command => command.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
                && command.Contains("collection_task_outbox", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(aggregateSql, "COUNT(");
            StringAssert.Contains(aggregateSql, "MIN(");
            StringAssert.Contains(aggregateSql, "LIMIT ");
            Assert.IsFalse(aggregateSql.Contains("SELECT \"c0\".*", StringComparison.OrdinalIgnoreCase),
                "The telemetry query must not select/materialize task entities before grouping.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task SlowTelemetryRead_DoesNotHoldBusinessGateOrDelayAcquire()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dispatch-snapshot-concurrency", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var resolver = new BlockingRaceIdentityResolver();
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }),
                resolver);
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            await store.RegisterDefinitionAsync(new("horse-detail"), "Horse detail", CollectionResourceType.Horse,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow;
            await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "20260928:tokyo:1"),
                new("race-detail"), 1, CollectionReason.Initial, now.AddMinutes(-1));
            var horseReceipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "horse-telemetry-test"),
                new("horse-detail"), 1, CollectionReason.Initial, now.AddMinutes(-1));

            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                db.RaceRepairHolds.Add(new RaceRepairHoldEntity
                {
                    RaceId = "race-held-by-test",
                    Generation = 1,
                    OperationId = Guid.NewGuid().ToString("D"),
                    Reason = "test",
                    CreatedAt = now,
                });
                await db.SaveChangesAsync();
            }

            resolver.Block = true;
            var snapshotTask = store.GetDispatchTelemetrySnapshotAsync(now, 4, ["race-detail"], 0);
            await resolver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            long generation;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
                generation = await db.Tasks.Where(task => task.TaskId == horseReceipt.TaskId)
                    .Select(task => task.DispatchGeneration).SingleAsync();

            var acquireTask = store.AcquireAsync(horseReceipt.TaskId!.Value, generation, now,
                TimeSpan.FromMinutes(1));
            var completed = await Task.WhenAny(acquireTask, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.AreSame(acquireTask, completed,
                "A blocked telemetry snapshot must not own the store-wide semaphore or hold a SQLite writer lock.");
            Assert.IsNotNull(await acquireTask);
            resolver.Release.Set();
            await snapshotTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            resolver.Release.Set();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public void Clear() => Commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BlockingRaceIdentityResolver : IRaceResourceIdentityResolver
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public volatile bool Block;

        public string? Resolve(string resourceId, IReadOnlyDictionary<string, string> attributes)
        {
            if (Block)
            {
                Entered.TrySetResult();
                Release.Wait(TimeSpan.FromSeconds(10));
            }
            return "race-held-by-test";
        }
    }
}

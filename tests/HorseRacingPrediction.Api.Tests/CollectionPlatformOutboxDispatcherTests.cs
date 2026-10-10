using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlatformOutboxDispatcherTests
{
    [TestMethod]
    public async Task DefinitelyFullExecutionCapacitySkipsCandidateAndIdentityQueries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-capacity-hint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var capture = new DispatchSqlCaptureInterceptor();
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False")
                .AddInterceptors(capture).Options;
            var store = new CollectionPlatformStore(dbOptions);
            var now = DateTimeOffset.UtcNow;
            await store.RegisterDefinitionAsync(new("horse-profile"), "horse", CollectionResourceType.Horse, 1,
                "initial", false);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "capacity-hint"),
                new("horse-profile"), 1, CollectionReason.Initial, now);
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                db.ExecutionLeases.Add(new CollectionExecutionLeaseEntity
                {
                    ExecutionBatchId = Guid.NewGuid(),
                    DispatchEnvelopeId = Guid.NewGuid(),
                    WakeId = Guid.NewGuid(),
                    ReservationToken = "active-capacity-hint",
                    LeaseToken = "active-capacity-hint",
                    Status = "Running",
                    LeaseExpiresAt = now.AddMinutes(1),
                    CreatedAt = now,
                    StartedAt = now,
                });
                await db.SaveChangesAsync();
            }
            Assert.IsTrue(await store.IsExecutionCapacityDefinitelyFullAsync(1, now));
            capture.Commands.Clear();
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    AggregationDelayMilliseconds = 0,
                    MaxInFlightEnvelopes = 1,
                }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            Assert.IsEmpty(queue.Messages);
            Assert.IsFalse(capture.Commands.Any(command => command.Contains("collection_task_outbox",
                    StringComparison.OrdinalIgnoreCase)),
                "A full live-lease lower bound must return before pending, capacity-category, or reservation queries.");
            Assert.IsFalse(capture.Commands.Any(command => command.Contains("collection_resources",
                    StringComparison.OrdinalIgnoreCase)),
                "A full-capacity dispatcher cycle must not resolve candidate identities.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task HostedLoop_RetriesAfterCycleFailure_AndDispatchesWithoutRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-cycle-retry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("horse-profile"), "horse", CollectionResourceType.Horse, 1, "initial", false);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H-cycle"), new("horse-profile"),
                1, CollectionReason.Initial, DateTimeOffset.UtcNow.AddMinutes(-1));
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchIntervalSeconds = 1,
                    DispatchBatchSize = 1,
                    AggregationDelayMilliseconds = 0
                }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var cycles = 0;
            dispatcher.BeforeCycleAsync = _ => Interlocked.Increment(ref cycles) == 1
                ? Task.FromException(new InvalidOperationException("fail once before dispatch"))
                : Task.CompletedTask;

            await dispatcher.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => queue.Messages.Count == 1, TimeSpan.FromSeconds(4));
            await dispatcher.StopAsync(CancellationToken.None);

            Assert.IsGreaterThanOrEqualTo(2, cycles);
            Assert.HasCount(1, queue.Messages);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task WakeQueue_ReceivesOnlyOpaqueReservationIdentifiers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-wake", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("horse-profile"), "horse", CollectionResourceType.Horse, 1, "initial", false);
            var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H1"), new("horse-profile"),
                1, CollectionReason.Initial, DateTimeOffset.UtcNow.AddMinutes(-1));
            var queue = new WakeRecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    AggregationDelayMilliseconds = 0
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var wake = queue.Wakes.Single();
            Assert.AreNotEqual(Guid.Empty, wake.WakeId);
            Assert.AreNotEqual(Guid.Empty, wake.DispatchEnvelopeId);
            Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(wake).Contains(
                receipt.TaskId!.Value.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CompatibleRaceCards_AreSentInOneStableEnvelope()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-envelope", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", CollectionResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var date = new DateOnly(2026, 9, 12);
            for (var index = 12; index >= 1; index--)
                await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", $"R{index}"), new("race-card"), 1,
                    CollectionReason.Initial, now.AddSeconds(index), CollectionLane.Realtime, index,
                    effectiveDate: date);
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 12,
                    AggregationDelayMilliseconds = 0
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var envelope = queue.Messages.Single();
            Assert.HasCount(12, envelope.Tasks);
            Assert.AreEqual("JRA", envelope.Compatibility.Provider);
            Assert.AreEqual(new CollectionDefinitionId("race-card"), envelope.Compatibility.Definition);
            Assert.AreEqual(date, envelope.Compatibility.EffectiveDate);
            var priorities = (await store.GetTasksAsync()).ToDictionary(x => x.TaskId, x => x.Priority);
            CollectionAssert.AreEqual(priorities.OrderByDescending(x => x.Value).Select(x => x.Key).ToArray(),
                envelope.Tasks.Select(x => x.TaskId).ToArray());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task ActualDispatcher_PrioritizesRealtimeButDispatchesBackgroundAfterFour()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-dispatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", CollectionResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            for (var index = 0; index < 5; index++)
                await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", $"R{index}"), new("race-card"), 1,
                    CollectionReason.Initial, now, CollectionLane.Realtime, 100);
            await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", "BACKGROUND"), new("race-card"), 1,
                CollectionReason.Backfill, now, CollectionLane.Background, 10);
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 1,
                    MaxInFlightEnvelopes = 10
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            for (var count = 0; count < 4; count++) await dispatcher.DispatchOnceAsync(CancellationToken.None);
            var restarted = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 1,
                    MaxInFlightEnvelopes = 10
                }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            await restarted.DispatchOnceAsync(CancellationToken.None);

            var tasks = await store.GetTasksAsync();
            var backgroundId = tasks.Single(x => x.Resource.Id == "BACKGROUND").TaskId;
            Assert.AreEqual(backgroundId, queue.Messages[4].Tasks.Single().TaskId);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task ActualDispatcher_PersistsMixedLaneRotationAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-dispatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", CollectionResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            for (var index = 0; index < 8; index++)
                await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", $"R{index}"), new("race-card"), 1,
                    CollectionReason.Initial, now, CollectionLane.Realtime, 100);
            await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", "NORMAL"), new("race-card"), 1,
                CollectionReason.Recovery, now, CollectionLane.Normal, 50);
            await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", "BACKGROUND"), new("race-card"), 1,
                CollectionReason.Backfill, now, CollectionLane.Background, 10);
            var queue = new RecordingQueue();
            var options = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 20
            });
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue, options,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            for (var count = 0; count < 5; count++) await dispatcher.DispatchOnceAsync(CancellationToken.None);
            dispatcher = new CollectionPlatformOutboxDispatcher(store, queue, options,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            for (var count = 0; count < 5; count++) await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var tasks = (await store.GetTasksAsync()).ToDictionary(x => x.TaskId);
            var lanes = queue.Messages.Select(x => tasks[x.Tasks.Single().TaskId].Lane).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime,
                CollectionLane.Normal,
                CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime, CollectionLane.Realtime,
                CollectionLane.Background,
            }, lanes);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CapacityOne_WaitsForCurrentEnvelopeBeforeSendingNext()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-capacity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", CollectionResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", "R1"), new("race-card"), 1,
                CollectionReason.Initial, now, CollectionLane.Realtime, 100, effectiveDate: new(2026, 9, 12));
            await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", "R2"), new("race-card"), 1,
                CollectionReason.Initial, now, CollectionLane.Realtime, 100, effectiveDate: new(2026, 9, 13));
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 10,
                    MaxInFlightEnvelopes = 1,
                    AggregationDelayMilliseconds = 0
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            Assert.HasCount(1, queue.Messages);

            var dispatchedTaskId = queue.Messages.Single().Tasks.Single().TaskId;
            var lease = await store.AcquireAsync(dispatchedTaskId, 1, now, TimeSpan.FromMinutes(5));
            Assert.IsNotNull(lease);
            Assert.IsTrue(await store.CompleteAttemptAsync(dispatchedTaskId, lease.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.Succeeded)));
            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            Assert.HasCount(2, queue.Messages);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task SameRaceDay_CrossDefinitionTasksShareVersionTwoEnvelope()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-race-day", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", CollectionResourceType.RaceCard, 1, "initial", false);
            await store.RegisterDefinitionAsync(new("race-result"), "result", CollectionResourceType.RaceResult, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var date = new DateOnly(2026, 9, 13);
            var attributes = new Dictionary<string, string> { ["course"] = "Tokyo", ["number"] = "1" };
            await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", "CARD"), new("race-card"), 1,
                CollectionReason.Initial, now, CollectionLane.Realtime, 80, effectiveDate: date, attributes: attributes);
            await store.RequestAsync(new(CollectionResourceType.RaceResult, "JRA", "RESULT"), new("race-result"), 1,
                CollectionReason.Initial, now, CollectionLane.Realtime, 100, effectiveDate: date, attributes: attributes);
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    AggregationDelayMilliseconds = 0,
                    MaxInFlightEnvelopes = 1,
                    RaceDayMaxTasks = 24
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var envelope = queue.Messages.Single();
            Assert.AreEqual(2, envelope.ContractVersion);
            Assert.AreEqual(CollectionDispatchGroupKind.RaceDay, envelope.Compatibility.GroupKind);
            Assert.AreEqual("2026-09-13", envelope.Compatibility.GroupKey);
            Assert.HasCount(2, envelope.Tasks);
            var tasks = (await store.GetTasksAsync()).ToDictionary(x => x.Resource.Type, x => x.TaskId);
            CollectionAssert.AreEqual(new[] { tasks[CollectionResourceType.RaceCard], tasks[CollectionResourceType.RaceResult] },
                envelope.Tasks.Select(x => x.TaskId).ToArray());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task SameWeekendHorses_ShareWeekendSubjectEnvelope()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-weekend", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("horse-profile"), "horse", CollectionResourceType.Horse, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var attributes = new Dictionary<string, string> { ["weekendPriorityUntil"] = "2026-09-13" };
            for (var index = 1; index <= 2; index++)
                await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", $"H{index}"), new("horse-profile"), 1,
                    CollectionReason.Discovery, now, CollectionLane.Realtime, 80, attributes: attributes);
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    AggregationDelayMilliseconds = 0,
                    MaxInFlightEnvelopes = 1,
                    WeekendSubjectsMaxTasks = 12
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var envelope = queue.Messages.Single();
            Assert.AreEqual(CollectionDispatchGroupKind.WeekendSubjects, envelope.Compatibility.GroupKind);
            Assert.AreEqual("2026-09-13", envelope.Compatibility.GroupKey);
            Assert.HasCount(2, envelope.Tasks);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task ConcurrentDispatchers_ReserveOnlyOneCapacitySlot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-concurrent-capacity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var store1 = new CollectionPlatformStore(options);
            var store2 = new CollectionPlatformStore(options);
            await store1.RegisterDefinitionAsync(new("race-card"), "card", CollectionResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            for (var index = 1; index <= 2; index++)
                await store1.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", $"R{index}"), new("race-card"), 1,
                    CollectionReason.Initial, now, CollectionLane.Realtime, 80,
                    effectiveDate: new DateOnly(2026, 9, 10 + index));
            var queue = new RecordingQueue();
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                AggregationDelayMilliseconds = 0,
                MaxInFlightEnvelopes = 1
            });
            var first = new CollectionPlatformOutboxDispatcher(store1, queue, queueOptions,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var second = new CollectionPlatformOutboxDispatcher(store2, queue, queueOptions,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await Task.WhenAll(first.DispatchOnceAsync(CancellationToken.None),
                second.DispatchOnceAsync(CancellationToken.None));

            Assert.HasCount(1, queue.Messages);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task RaceDayAboveLimit_SendsOnlyFirstChunkAndLeavesRemainderInDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-race-day-limit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", CollectionResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var date = new DateOnly(2026, 9, 13);
            for (var index = 1; index <= 26; index++)
                await store.RequestAsync(new(CollectionResourceType.RaceCard, "JRA", $"R{index}"), new("race-card"), 1,
                    CollectionReason.Initial, now, CollectionLane.Realtime, index >= 25 ? 100 : 10,
                    effectiveDate: date,
                    attributes: new Dictionary<string, string>
                    {
                        ["course"] = index <= 12 ? "Nakayama" : "Hanshin",
                        ["number"] = ((index - 1) % 12 + 1).ToString()
                    });
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    AggregationDelayMilliseconds = 0,
                    MaxInFlightEnvelopes = 1,
                    RaceDayMaxTasks = 24
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            Assert.HasCount(1, queue.Messages);
            Assert.HasCount(24, queue.Messages.Single().Tasks);
            var tasks = (await store.GetTasksAsync(limit: 100)).ToDictionary(x => x.Resource.Id, x => x.TaskId);
            Assert.IsTrue(queue.Messages.Single().Tasks.Any(x => x.TaskId == tasks["R25"]));
            Assert.IsTrue(queue.Messages.Single().Tasks.Any(x => x.TaskId == tasks["R26"]));
            Assert.HasCount(2, await store.GetPendingDispatchesAsync(DateTimeOffset.UtcNow, 100));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class RecordingQueue : ICollectionPlatformTaskQueue
    {
        private readonly object _gate = new();
        public List<CollectionDispatchEnvelope> Messages { get; } = [];
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            lock (_gate) Messages.Add(envelope);
            return Task.FromResult(new CollectionQueueSendReceipt(Guid.NewGuid().ToString("N")));
        }
    }

    private sealed class DispatchSqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(25);
        Assert.IsTrue(condition(), "The expected dispatcher activity did not occur before timeout.");
    }

    private sealed class WakeRecordingQueue : ICollectionPlatformTaskQueue
    {
        public List<CollectionWakeSignal> Wakes { get; } = [];
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new AssertFailedException("Task envelopes are not allowed.");
        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken)
        {
            Wakes.Add(wake);
            return Task.FromResult(new CollectionQueueSendReceipt("wake-message"));
        }
    }
}

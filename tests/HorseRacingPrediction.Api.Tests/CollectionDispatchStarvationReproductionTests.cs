using System.Text.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Tests;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionDispatchStarvationReproductionTests
{
    private const string ExpectFixedEnvironmentVariable = "COLLECTION_STARVATION_EXPECT_FIXED";

    [TestMethod]
    [DataRow("stale-generation")]
    [DataRow("terminal-task")]
    public async Task WakeOnlyReservation_CharacterizesStarvationBeforeFix(string invalidCandidate)
    {
        var expectFixed = string.Equals(Environment.GetEnvironmentVariable(ExpectFixedEnvironmentVariable), "1",
            StringComparison.Ordinal);
        var root = Path.Combine(Path.GetTempPath(), "collection-starvation-repro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var eventDatabase = Path.Combine(root, "events.db");
        var (app, http) = await TestApplicationFactory.CreateAsync($"Data Source={eventDatabase}");
        try
        {
            http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
            var store = app.Services.GetRequiredService<CollectionPlatformStore>();
            var now = DateTimeOffset.UtcNow.AddMinutes(-5);
            var definition = new CollectionDefinitionId("race-detail");
            await store.RegisterDefinitionAsync(definition, "Race detail", CollectionResourceType.Race,
                1, "initial", false);

            var invalidReceipt = await store.RequestAsync(
                new(CollectionResourceType.Race, "JRA", $"INVALID-{invalidCandidate}"), definition, 1,
                CollectionReason.Initial, now, CollectionLane.Background, 100);
            var backgroundReceipt = await store.RequestAsync(
                new(CollectionResourceType.Race, "JRA", "VALID-BACKGROUND"), definition, 1,
                CollectionReason.Backfill, now, CollectionLane.Background, 10);

            var databasePath = Path.Combine(Path.GetFullPath(eventDatabase) + ".collection", "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var invalidTask = await db.Tasks.SingleAsync(x => x.TaskId == invalidReceipt.TaskId);
                var invalidOutbox = await db.DispatchOutbox.SingleAsync(x => x.TaskId == invalidReceipt.TaskId);
                if (invalidCandidate == "stale-generation")
                {
                    Assert.AreEqual(CollectionTaskStatus.Ready, invalidTask.Status);
                    invalidOutbox.DispatchGeneration = invalidTask.DispatchGeneration + 1;
                }
                else
                {
                    invalidTask.Status = CollectionTaskStatus.DeadLetter;
                    invalidOutbox.DispatchGeneration = invalidTask.DispatchGeneration;
                }
                await db.SaveChangesAsync();
            }

            var queue = new LocalWakeQueue();
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                DispatchBatchSize = 1,
                EnvelopeMaxTasks = 1,
                MaxInFlightEnvelopes = 1,
                OutboxReservationSeconds = 45,
                AggregationDelayMilliseconds = 0
            });
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue, queueOptions,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);
            var worker = new CollectionPlatformWorkerClient(http,
                new CollectionDefinitionHandlerRegistry(Array.Empty<ICollectionDefinitionHandler>()));

            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            Assert.AreEqual(1, queue.Wakes.Count, "The first real dispatch cycle must send exactly one wake.");
            var wake = queue.Wakes.Single();
            var selectedOutbox = await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId);
            if (!expectFixed)
                Assert.AreEqual(invalidReceipt.TaskId, selectedOutbox.TaskId,
                    "The higher-priority invalid candidate must be selected before the Background task.");
            var delivery = queue.ReceiveNext();

            var acquire = await worker.AcquireNextAsync(wake, delivery.MessageId, CancellationToken.None);
            if (!expectFixed)
                Assert.AreEqual(CollectionExecutionAcquireStatus.NoWork, acquire.Status,
                    "The real HTTP acquire endpoint must reject the selected stale/ineligible row as typed NoWork.");
            var lambdaResponse = await CollectionLambdaInvocation.ExecuteWakeAsync(
                CreateSqsWakeEvent(delivery), worker,
                cancellationToken: CancellationToken.None,
                executeGroup: expectFixed ? static (_, _, _) => Task.CompletedTask : null);
            Assert.AreEqual(0, lambdaResponse.BatchItemFailures.Count,
                "The Lambda invocation must acknowledge the typed acquire result.");
            queue.ApplyBatchResponse(lambdaResponse);
            Assert.AreEqual(0, queue.VisibleCount,
                "A typed NoWork or successfully acquired batch must acknowledge its local queue message.");

            var reserved = await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId);
            if (!expectFixed)
            {
                Assert.IsNotNull(reserved.ReservationToken, "NoWork must leave the wake reservation live in the baseline.");
                Assert.IsTrue(reserved.ReservedUntilUnixMilliseconds > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    "The invalid reservation must still be unexpired during the bounded reproduction window.");
                Assert.AreEqual(CollectionLaneDispatchState.Empty,
                    await store.GetLaneDispatchStateAsync(CancellationToken.None),
                    "A wake-only reservation does not advance persisted dispatched-lane state.");
            }

            for (var cycle = 0; cycle < 2; cycle++)
            {
                await dispatcher.DispatchOnceAsync(CancellationToken.None);
                Assert.AreEqual(1, queue.Wakes.Count,
                    expectFixed
                        ? "The sole valid Background outbox row must not be emitted more than once."
                        : "Capacity one must remain occupied, preventing a new wake for the valid Background row.");
            }

            await using (var db = new CollectionPlatformDbContext(dbOptions))
            {
                var backgroundOutbox = await db.DispatchOutbox.SingleAsync(x => x.TaskId == backgroundReceipt.TaskId);
                if (expectFixed)
                {
                    Assert.IsNotNull(backgroundOutbox.DispatchedAt,
                        "The valid Background task must be acquired within the bounded poll window.");
                    Assert.IsTrue(await db.ExecutionLeases.AnyAsync(x => x.DispatchEnvelopeId == wake.DispatchEnvelopeId),
                        "The bounded poll window must create an execution lease for the selected valid wake.");
                }
                else
                {
                    Assert.IsNull(backgroundOutbox.ReservationToken,
                        "The valid Background outbox row must remain undispatched and unreserved.");
                    Assert.IsFalse(await db.ExecutionLeases.AnyAsync(x => x.DispatchEnvelopeId == wake.DispatchEnvelopeId),
                        "No execution lease may be created for the rejected wake.");
                }
            }
        }
        finally
        {
            http.Dispose();
            await app.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task RaceOddsBypassesAggregationDelay_WhileOtherDefinitionsRemainDelayed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-race-odds-delay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-odds"), "Race odds", CollectionResourceType.RaceOdds,
                1, "initial", false);
            await store.RegisterDefinitionAsync(new("race-result"), "Race result", CollectionResourceType.RaceResult,
                1, "initial", false);
            var requestedAt = DateTimeOffset.UtcNow;
            var odds = await store.RequestAsync(new(CollectionResourceType.RaceOdds, "JRA", "ODDS"),
                new("race-odds"), 1, CollectionReason.Initial, requestedAt, CollectionLane.Background, 10);
            var result = await store.RequestAsync(new(CollectionResourceType.RaceResult, "JRA", "RESULT"),
                new("race-result"), 1, CollectionReason.Initial, requestedAt, CollectionLane.Background, 10);
            var queue = new WakeCaptureQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 1,
                    MaxInFlightEnvelopes = 1,
                    AggregationDelayMilliseconds = 60_000
                }), NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var wake = queue.Wakes.Single();
            var databasePath = Path.Combine(directory, "collection-platform.db");
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30").Options;
            var dispatched = await LoadOutboxForWakeAsync(dbOptions, wake.DispatchEnvelopeId);
            Assert.AreEqual(odds.TaskId, dispatched.TaskId,
                "race-odds must dispatch immediately despite the configured aggregation delay.");
            await using var db = new CollectionPlatformDbContext(dbOptions);
            var delayed = await db.DispatchOutbox.SingleAsync(x => x.TaskId == result.TaskId);
            Assert.IsNull(delayed.ReservationToken,
                "A similarly aged non-race-odds definition must remain delayed and unreserved.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<CollectionDispatchOutboxEntity> LoadOutboxForWakeAsync(
        DbContextOptions<CollectionPlatformDbContext> dbOptions, Guid envelopeId)
    {
        await using var db = new CollectionPlatformDbContext(dbOptions);
        return await db.DispatchOutbox.SingleAsync(x => x.EnvelopeId == envelopeId);
    }

    private static string CreateSqsWakeEvent(LocalWakeQueue.LocalMessage message)
    {
        return JsonSerializer.Serialize(new
        {
            Records = new[] { new { messageId = message.MessageId, body = message.Body } }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private sealed class LocalWakeQueue : ICollectionPlatformTaskQueue
    {
        private readonly List<LocalMessage> _messages = [];
        public List<CollectionWakeSignal> Wakes { get; } = [];
        public int VisibleCount => _messages.Count;

        public sealed record LocalMessage(string MessageId, string Body);

        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new AssertFailedException("Only wake-only messages are expected.");

        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken)
        {
            Wakes.Add(wake);
            var messageId = $"local-message-{Wakes.Count}";
            _messages.Add(new LocalMessage(messageId,
                JsonSerializer.Serialize(wake, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            return Task.FromResult(new CollectionQueueSendReceipt(messageId));
        }

        public LocalMessage ReceiveNext() => _messages.Single();

        public void ApplyBatchResponse(CollectionLambdaBatchResponse response)
        {
            var failedIds = response.BatchItemFailures.Select(x => x.ItemIdentifier).ToHashSet(StringComparer.Ordinal);
            _messages.RemoveAll(x => !failedIds.Contains(x.MessageId));
        }
    }

    private sealed class WakeCaptureQueue : ICollectionPlatformTaskQueue
    {
        public List<CollectionWakeSignal> Wakes { get; } = [];

        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new AssertFailedException("Only wake-only messages are expected.");

        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken)
        {
            Wakes.Add(wake);
            return Task.FromResult(new CollectionQueueSendReceipt($"wake-{Wakes.Count}"));
        }
    }
}

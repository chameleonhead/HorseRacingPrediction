using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlatformOperationsServicesTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "collection-platform-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task DlqReconciler_AuditsWakeWithoutChangingTaskAndDeletesMessage()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var queue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("receipt-1", JsonSerializer.Serialize(
            new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

        Assert.AreEqual(1, await service.RunOnceAsync(CancellationToken.None));
        CollectionAssert.AreEqual(new[] { "receipt-1" }, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
    }

    [TestMethod]
    public async Task DlqReconciler_RetainsMalformedMessageForOperatorInspection()
    {
        var store = await CreateStoreAsync();
        var queue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("receipt-bad", "not-json"));
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

        Assert.AreEqual(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
    }

    [TestMethod]
    public async Task DlqReconciler_RetainsLegacyTaskNotificationWithoutChangingTask()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var notification = new CollectionTaskNotification(taskId, 1);
        var queue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("legacy-v1",
            JsonSerializer.Serialize(notification, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var service = CreateReconciler(store, queue);

        Assert.AreEqual(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
        Assert.HasCount(0, await store.GetActionableFailureNotificationsAsync(
            DateTimeOffset.UtcNow.AddMinutes(1), 10));
    }

    [TestMethod]
    public async Task DlqReconciler_AuditsMultipleWakeSignals()
    {
        var store = await CreateStoreAsync();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var queue = new RecordingQueue(
            new CollectionPlatformDeadLetterMessage("wake-1", JsonSerializer.Serialize(
                new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease-1"), jsonOptions)),
            new CollectionPlatformDeadLetterMessage("wake-2", JsonSerializer.Serialize(
                new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease-2"), jsonOptions)));
        var service = CreateReconciler(store, queue);

        Assert.AreEqual(2, await service.RunOnceAsync(CancellationToken.None));
        CollectionAssert.AreEquivalent(new[] { "wake-1", "wake-2" }, queue.Deleted);
    }

    [TestMethod]
    public async Task DlqReconciler_DeletesRepeatedWakeWithoutCreatingTaskFailure()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var body = JsonSerializer.Serialize(new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.AreEqual(1, await CreateReconciler(store,
            new RecordingQueue(new CollectionPlatformDeadLetterMessage("first", body)))
            .RunOnceAsync(CancellationToken.None));
        var repeatedQueue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("repeated", body));
        Assert.AreEqual(1, await CreateReconciler(store, repeatedQueue).RunOnceAsync(CancellationToken.None));

        CollectionAssert.AreEqual(new[] { "repeated" }, repeatedQueue.Deleted);
        Assert.HasCount(0, await store.GetActionableFailureNotificationsAsync(
            DateTimeOffset.UtcNow.AddMinutes(1), 10));
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
    }

    [TestMethod]
    public async Task DlqReconciler_DiscardsLegacyOrInvalidNotificationWithoutChangingTasks()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var queue = new RecordingQueue(
            new CollectionPlatformDeadLetterMessage("legacy", $$"""{"taskId":"{{taskId}}","dispatchGeneration":1}"""),
            new CollectionPlatformDeadLetterMessage("invalid", JsonSerializer.Serialize(
                CreateEnvelope(Guid.Empty, 0), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

        Assert.AreEqual(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
    }

    [TestMethod]
    public async Task DlqReconciler_RetainsLegacyNotificationWithUnknownOrAmbiguousShape()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var queue = new RecordingQueue(
            new CollectionPlatformDeadLetterMessage("unknown-version",
                $$"""{"taskId":"{{taskId}}","dispatchGeneration":1,"contractVersion":2}"""),
            new CollectionPlatformDeadLetterMessage("ambiguous",
                $$"""{"taskId":"{{taskId}}","dispatchGeneration":1,"contractVersion":1,"extra":true}"""));

        Assert.AreEqual(0, await CreateReconciler(store, queue).RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
    }

    [TestMethod]
    public async Task ScheduleService_RunOnceEvaluatesWindowSequentiallyAndDeduplicatesArrivalAfterSnapshot()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));
        var activeResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "schedule-active");
        var scheduledResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "schedule-new");
        var lateResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "schedule-late");
        var activeReceipt = await store.RequestAsync(activeResource, new("horse-profile"), 1,
            CollectionReason.Initial, now.AddMinutes(-3));

        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False").Options;
        await using (var db = new CollectionPlatformDbContext(options))
        {
            var resources = new[] { scheduledResource, lateResource }
                .Select(resource => new CollectionResourceEntity
                {
                    Type = resource.Type,
                    Provider = resource.Provider,
                    ResourceId = resource.Id,
                    AttributesJson = "{}",
                    CreatedAt = now,
                }).ToArray();
            db.Resources.AddRange(resources);
            await db.SaveChangesAsync();

            var active = await db.Resources.SingleAsync(x => x.ResourceId == activeResource.Id);
            db.States.Single(x => x.ResourcePk == active.ResourcePk && x.DefinitionId == "horse-profile")
                .NextCollectionAt = now.AddSeconds(-3);
            foreach (var (resource, index) in resources.Select((resource, index) => (resource, index)))
            {
                db.States.Add(new CollectionStateEntity
                {
                    ResourcePk = resource.ResourcePk,
                    DefinitionId = "horse-profile",
                    AppliedRevision = 1,
                    RequiredRevision = 1,
                    LastCollectedAt = now.AddDays(-1),
                    NextCollectionAt = now.AddSeconds(-2 + index),
                    Status = CollectionStateStatus.RefreshDue,
                    UpdatedAt = now,
                });
            }
            await db.SaveChangesAsync();
        }

        CollectionRequestReceipt? lateArrival = null;
        var policy = new RecordingSchedulePolicy(state =>
        {
            if (state.Resource == lateResource && lateArrival is null)
                lateArrival = store.RequestAsync(lateResource, new("horse-profile"), 1,
                    CollectionReason.Initial, now).GetAwaiter().GetResult();
        });
        var service = new CollectionScheduleService(store, [policy],
            NullLogger<CollectionScheduleService>.Instance);

        await service.RunOnceAsync(now, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { activeResource.Id, scheduledResource.Id, lateResource.Id },
            policy.EvaluatedResourceIds);
        Assert.IsNotNull(activeReceipt.TaskId);
        Assert.IsNotNull(lateArrival?.TaskId);
        var tasks = await store.GetTasksAsync();
        Assert.HasCount(3, tasks);
        Assert.AreEqual(1, tasks.Count(x => x.Resource == activeResource));
        Assert.AreEqual(1, tasks.Count(x => x.Resource == scheduledResource));
        Assert.AreEqual(1, tasks.Count(x => x.Resource == lateResource));
        Assert.AreEqual(lateArrival!.TaskId, tasks.Single(x => x.Resource == lateResource).TaskId,
            "The RequestAsync guard must reuse the task created after the candidate snapshot.");
    }

    [TestMethod]
    public async Task ScheduleService_RunOnceHonorsCancellationBeforeScheduling()
    {
        var store = await CreateStoreAsync();
        var policy = new RecordingSchedulePolicy();
        var service = new CollectionScheduleService(store, [policy],
            NullLogger<CollectionScheduleService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RunOnceAsync(DateTimeOffset.UtcNow, cancellation.Token));

        Assert.IsEmpty(policy.EvaluatedResourceIds);
        Assert.IsEmpty(await store.GetTasksAsync());
    }

    private async Task<CollectionPlatformStore> CreateStoreAsync()
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = _directory
        }));
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", CollectionResourceType.Horse,
            1, "initial", false);
        return store;
    }

    private static CollectionDispatchEnvelope CreateEnvelope(Guid taskId, long generation) => new(Guid.NewGuid(),
        new("JRA", new("horse-profile"), null, CollectionLane.Normal), [new(taskId, generation)]);

    private sealed class RecordingSchedulePolicy(Action<CollectionStateSnapshot>? onEvaluate = null)
        : ICollectionSchedulePolicy
    {
        public List<string> EvaluatedResourceIds { get; } = [];

        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
        {
            EvaluatedResourceIds.Add(resource.Id);
            onEvaluate?.Invoke(state);
            return new(true, state.NextCollectionAt, CollectionPriority.Normal, CollectionLane.Normal, "test");
        }
    }

    private static CollectionPlatformDeadLetterReconciler CreateReconciler(CollectionPlatformStore store,
        ICollectionPlatformTaskQueue queue) => new(store, queue,
        Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
        NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

    private sealed class RecordingQueue(params CollectionPlatformDeadLetterMessage[] messages)
        : ICollectionPlatformTaskQueue
    {
        public List<string> Deleted { get; } = [];
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken)
            => Task.FromResult(new CollectionQueueSendReceipt(null));
        public Task<IReadOnlyList<CollectionPlatformDeadLetterMessage>> ReceiveDeadLetterMessagesAsync(
            int maxMessages, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CollectionPlatformDeadLetterMessage>>(messages.Take(maxMessages).ToList());
        public Task DeleteDeadLetterMessageAsync(string receiptHandle, CancellationToken cancellationToken)
        {
            Deleted.Add(receiptHandle);
            return Task.CompletedTask;
        }
    }
}

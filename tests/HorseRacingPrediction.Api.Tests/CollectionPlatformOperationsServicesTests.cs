using System.Text.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "jra", "H1"), new("horse-profile"),
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
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "jra", "H1"), new("horse-profile"),
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
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "jra", "H1"), new("horse-profile"),
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
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "jra", "H1"), new("horse-profile"),
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
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "jra", "H1"), new("horse-profile"),
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

    private async Task<CollectionPlatformStore> CreateStoreAsync()
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = _directory
        }));
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse,
            1, "initial", false);
        return store;
    }

    private static CollectionDispatchEnvelope CreateEnvelope(Guid taskId, long generation) => new(Guid.NewGuid(),
        new("JRA", new("horse-profile"), null, CollectionLane.Normal), [new(taskId, generation)]);

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

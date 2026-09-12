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
    public async Task DlqReconciler_DeadLettersCurrentTaskAndDeletesMessage()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var queue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("receipt-1", JsonSerializer.Serialize(
            new CollectionTaskNotification(receipt.TaskId, 1), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

        Assert.AreEqual(1, await service.RunOnceAsync(CancellationToken.None));
        CollectionAssert.AreEqual(new[] { "receipt-1" }, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.DeadLetter,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId).Status);
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
    public async Task DlqReconciler_DiscardsLegacyOrInvalidNotificationWithoutChangingTasks()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var queue = new RecordingQueue(
            new CollectionPlatformDeadLetterMessage("legacy", $$"""{"taskId":"{{receipt.TaskId}}","dispatchGeneration":1}"""),
            new CollectionPlatformDeadLetterMessage("invalid", JsonSerializer.Serialize(
                new CollectionTaskNotification(Guid.Empty, 0), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

        Assert.AreEqual(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId).Status);
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

    private sealed class RecordingQueue(params CollectionPlatformDeadLetterMessage[] messages)
        : ICollectionPlatformTaskQueue
    {
        public List<string> Deleted { get; } = [];
        public Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
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

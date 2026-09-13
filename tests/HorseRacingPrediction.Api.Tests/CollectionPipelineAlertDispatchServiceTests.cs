using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPipelineAlertDispatchServiceTests
{
    [TestMethod]
    public async Task UnexpectedTerminalFailure_PausesAndPublishesOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hrp-alert-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var definition = new CollectionDefinitionId("horse-profile");
            await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", "horse-a"), definition, 1,
                CollectionReason.Initial, now, attributes: new Dictionary<string, string> { ["name"] = "A" });
            var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
            await store.CompleteAttemptAsync(receipt.TaskId, lease!.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.UnexpectedPage, "WrongPage", "unexpected page"));
            var publisher = new RecordingPublisher();
            var service = new CollectionPipelineAlertDispatchService(store, publisher,
                NullLogger<CollectionPipelineAlertDispatchService>.Instance);

            Assert.IsTrue((await store.GetPipelineStateAsync()).IsPaused);
            Assert.IsTrue(await service.RunOnceAsync(CancellationToken.None));
            Assert.IsFalse(await service.RunOnceAsync(CancellationToken.None));
            Assert.HasCount(1, publisher.Messages);
            StringAssert.Contains(publisher.Messages[0], receipt.TaskId.ToString("D"));
            Assert.IsEmpty(await store.GetUnpublishedFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class RecordingPublisher : ICollectionPipelineAlertPublisher
    {
        public List<string> Messages { get; } = [];
        public Task PublishCollectionStoppedAsync(string reason, int dlqFailureCount,
            CancellationToken cancellationToken)
        {
            Messages.Add(reason);
            return Task.CompletedTask;
        }
    }
}

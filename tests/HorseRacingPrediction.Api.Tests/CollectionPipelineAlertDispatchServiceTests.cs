using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Data.Sqlite;
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
            StringAssert.Contains(publisher.Messages[0], "TaskStatus=Failed");
            Assert.IsEmpty(await store.GetUnpublishedFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task PublishFailure_IsPersistedAndRetriedAfterServiceRecreation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hrp-alert-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var options = Options.Create(new CollectionPlatformOptions { StateDirectory = directory });
            var store = new CollectionPlatformStore(options);
            var definition = new CollectionDefinitionId("horse-profile");
            await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", "horse-a"), definition, 1,
                CollectionReason.Initial, now, attributes: new Dictionary<string, string> { ["name"] = "A" });
            var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
            await store.CompleteAttemptAsync(receipt.TaskId, lease!.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.UnexpectedPage, "WrongPage", "unexpected page"));
            var publisher = new RecordingPublisher { Failure = new InvalidOperationException("SNS unavailable") };
            var firstService = new CollectionPipelineAlertDispatchService(store, publisher,
                NullLogger<CollectionPipelineAlertDispatchService>.Instance);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => firstService.RunOnceAsync(CancellationToken.None));

            await using (var connection = new SqliteConnection(
                             $"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT PublishAttemptCount, LastPublishError, PublishedAt "
                    + "FROM collection_failure_notifications LIMIT 1";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.IsTrue(await reader.ReadAsync());
                Assert.AreEqual(1L, reader.GetInt64(0));
                Assert.AreEqual("SNS unavailable", reader.GetString(1));
                Assert.IsTrue(reader.IsDBNull(2));
            }

            publisher.Failure = null;
            var restartedStore = new CollectionPlatformStore(options);
            var restartedService = new CollectionPipelineAlertDispatchService(restartedStore, publisher,
                NullLogger<CollectionPipelineAlertDispatchService>.Instance);
            Assert.IsTrue(await restartedService.RunOnceAsync(CancellationToken.None));
            Assert.HasCount(2, publisher.Messages);
            Assert.IsEmpty(await restartedStore.GetUnpublishedFailureNotificationsAsync(
                DateTimeOffset.UtcNow.AddMinutes(1), 10));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class RecordingPublisher : ICollectionPipelineAlertPublisher
    {
        public List<string> Messages { get; } = [];
        public Exception? Failure { get; set; }
        public Task PublishCollectionStoppedAsync(string reason, int dlqFailureCount,
            CancellationToken cancellationToken)
        {
            Messages.Add(reason);
            if (Failure is not null) return Task.FromException(Failure);
            return Task.CompletedTask;
        }
    }
}

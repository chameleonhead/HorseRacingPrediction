using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class LocalCollectionQueueTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"local-collection-queue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public async Task Message_IsInvisibleUntilReleased_AndAckRemovesIt()
    {
        var queue = new LocalCollectionQueue(Path.Combine(_directory, "queue.db"));
        var notification = new CollectionTaskNotification(Guid.NewGuid(), 3);
        await queue.SendAsync(notification);

        var received = await queue.ReceiveAsync(TimeSpan.FromMinutes(1));
        Assert.IsNotNull(received);
        Assert.AreEqual(notification, received.Notification);
        Assert.IsNull(await queue.ReceiveAsync(TimeSpan.FromMinutes(1)));

        await queue.ReleaseAsync(received.ReceiptHandle);
        var redelivered = await queue.ReceiveAsync(TimeSpan.FromMinutes(1));
        Assert.IsNotNull(redelivered);
        Assert.AreEqual(2, redelivered.ReceiveCount);
        await queue.AcknowledgeAsync(redelivered.ReceiptHandle);
        Assert.AreEqual((0L, 0L, 0L), await queue.GetDepthAsync());
    }

    [TestMethod]
    public async Task Message_MovesToDeadLetterAfterMaximumReceives()
    {
        var queue = new LocalCollectionQueue(Path.Combine(_directory, "queue.db"));
        await queue.SendAsync(new(Guid.NewGuid(), 1));
        for (var count = 1; count <= 3; count++)
        {
            var message = await queue.ReceiveAsync(TimeSpan.FromMinutes(1));
            Assert.IsNotNull(message);
            await queue.ReleaseAsync(message.ReceiptHandle, 3);
        }

        Assert.AreEqual((0L, 0L, 1L), await queue.GetDepthAsync());
        Assert.IsNull(await queue.ReceiveAsync(TimeSpan.FromMinutes(1)));
    }
}

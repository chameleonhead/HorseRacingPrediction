using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using System.Text.Json;

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
        var envelope = CreateEnvelope(3);
        await queue.SendAsync(envelope);

        var received = await queue.ReceiveAsync(TimeSpan.FromMinutes(1));
        Assert.IsNotNull(received);
        AssertEnvelope(envelope, received.Envelope);
        Assert.IsNull(await queue.ReceiveAsync(TimeSpan.FromMinutes(1)));

        await queue.ReleaseAsync(received.ReceiptHandle);
        var redelivered = await queue.ReceiveAsync(TimeSpan.FromMinutes(1));
        Assert.IsNotNull(redelivered);
        AssertEnvelope(envelope, redelivered.Envelope);
        Assert.AreEqual(2, redelivered.ReceiveCount);
        await queue.AcknowledgeAsync(redelivered.ReceiptHandle);
        Assert.AreEqual((0L, 0L, 0L), await queue.GetDepthAsync());
    }

    [TestMethod]
    public async Task Message_MovesToDeadLetterAfterMaximumReceives()
    {
        var queue = new LocalCollectionQueue(Path.Combine(_directory, "queue.db"));
        await queue.SendAsync(CreateEnvelope(1));
        for (var count = 1; count <= 3; count++)
        {
            var message = await queue.ReceiveAsync(TimeSpan.FromMinutes(1));
            Assert.IsNotNull(message);
            await queue.ReleaseAsync(message.ReceiptHandle, 3);
        }

        Assert.AreEqual((0L, 0L, 1L), await queue.GetDepthAsync());
        Assert.IsNull(await queue.ReceiveAsync(TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public void Envelope_SerializesExplicitContractVersion()
    {
        var envelope = CreateEnvelope(4);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(envelope,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.AreEqual(CollectionDispatchEnvelope.CurrentContractVersion,
            json.RootElement.GetProperty("contractVersion").GetInt32());
        Assert.IsTrue(envelope.IsSupported());
        Assert.IsFalse((envelope with { ContractVersion = 99 }).IsSupported());
    }

    private static CollectionDispatchEnvelope CreateEnvelope(long generation) => new(Guid.NewGuid(),
        new("JRA", new("race-card"), new DateOnly(2026, 9, 12), CollectionLane.Realtime),
        [new(Guid.NewGuid(), generation)]);

    private static void AssertEnvelope(CollectionDispatchEnvelope expected, CollectionDispatchEnvelope actual)
    {
        Assert.AreEqual(expected.EnvelopeId, actual.EnvelopeId);
        Assert.AreEqual(expected.Compatibility, actual.Compatibility);
        CollectionAssert.AreEqual(expected.Tasks.ToArray(), actual.Tasks.ToArray());
    }
}

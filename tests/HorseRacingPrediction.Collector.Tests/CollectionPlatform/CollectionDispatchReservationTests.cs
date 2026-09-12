using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionDispatchReservationTests
{
    [TestMethod]
    public async Task Reservation_HidesRowsUntilExpiryAndOnlyOwnerCanFinalize()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dispatch-reservation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", ResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow;
            await store.RequestAsync(new(ResourceType.RaceCard, "JRA", "R1"), new("race-card"), 1,
                CollectionReason.Initial, now.AddMinutes(-1), effectiveDate: new DateOnly(2026, 9, 12));
            var pending = (await store.GetPendingDispatchesAsync(now, 10)).Single();
            var envelopeId = Guid.NewGuid();

            Assert.IsTrue(await store.TryReserveDispatchesAsync([pending.OutboxId], "owner", envelopeId,
                now, TimeSpan.FromMinutes(1)));
            Assert.IsEmpty(await store.GetPendingDispatchesAsync(now.AddSeconds(30), 10));
            Assert.IsFalse(await store.MarkDispatchedAsync([pending.OutboxId], "other", envelopeId,
                "message", now.AddSeconds(30)));
            Assert.HasCount(1, await store.GetPendingDispatchesAsync(now.AddMinutes(2), 10));
            Assert.IsTrue(await store.TryReserveDispatchesAsync([pending.OutboxId], "new-owner", envelopeId,
                now.AddMinutes(2), TimeSpan.FromMinutes(1)));
            Assert.IsTrue(await store.MarkDispatchedAsync([pending.OutboxId], "new-owner", envelopeId,
                "message", now.AddMinutes(2)));
            Assert.IsEmpty(await store.GetPendingDispatchesAsync(now.AddMinutes(4), 10));
        }
        finally { Directory.Delete(directory, true); }
    }
}

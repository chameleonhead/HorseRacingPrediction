using HorseRacingPrediction.PredictionScheduling;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class PredictionScheduleStoreTests
{
    [TestMethod]
    public async Task ExpiredLeaseIsRecoveredAcrossStoreRestart()
    {
        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.Parse("2026-09-11T10:00:00+09:00");
        var first = CreateStore(directory.Path);
        await first.EnqueueAsync(["race-1"], now);
        var acquired = await first.AcquireAsync(now.AddMinutes(10), TimeSpan.FromMinutes(10), 1,
            TimeSpan.FromMinutes(5));

        Assert.HasCount(1, acquired);
        var restarted = CreateStore(directory.Path);
        Assert.IsEmpty(await restarted.AcquireAsync(now.AddMinutes(14), TimeSpan.Zero, 1, TimeSpan.FromMinutes(5)));
        var recovered = await restarted.AcquireAsync(now.AddMinutes(16), TimeSpan.Zero, 1, TimeSpan.FromMinutes(5));
        Assert.HasCount(1, recovered);
        Assert.AreEqual("race-1", recovered[0].RaceId);
        Assert.AreNotEqual(acquired[0].LeaseToken, recovered[0].LeaseToken);
    }

    [TestMethod]
    public async Task StaleLeaseCannotCompleteReacquiredCandidate()
    {
        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.Parse("2026-09-11T10:00:00+09:00");
        var store = CreateStore(directory.Path);
        await store.EnqueueAsync(["race-1"], now);
        var first = (await store.AcquireAsync(now, TimeSpan.Zero, 1, TimeSpan.FromMinutes(5))).Single();
        var second = (await store.AcquireAsync(now.AddMinutes(6), TimeSpan.Zero, 1, TimeSpan.FromMinutes(5))).Single();

        Assert.IsFalse(await store.CompleteAsync(first.RaceId, first.LeaseToken));
        Assert.IsTrue(await store.CompleteAsync(second.RaceId, second.LeaseToken));
        Assert.IsEmpty(await store.AcquireAsync(now.AddHours(1), TimeSpan.Zero, 1, TimeSpan.FromMinutes(5)));
    }

    [TestMethod]
    public async Task RequeueUsesRequestedAvailability()
    {
        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.Parse("2026-09-11T10:00:00+09:00");
        var store = CreateStore(directory.Path);
        await store.EnqueueAsync(["race-1", "race-1"], now);
        var lease = (await store.AcquireAsync(now, TimeSpan.Zero, 10, TimeSpan.FromMinutes(5))).Single();
        Assert.IsTrue(await store.RequeueAsync(lease.RaceId, lease.LeaseToken, now.AddMinutes(15), "pending"));

        Assert.IsEmpty(await store.AcquireAsync(now.AddMinutes(14), TimeSpan.Zero, 10, TimeSpan.FromMinutes(5)));
        Assert.HasCount(1, await store.AcquireAsync(now.AddMinutes(15), TimeSpan.Zero, 10, TimeSpan.FromMinutes(5)));
    }

    private static PredictionScheduleStore CreateStore(string path) => new(Options.Create(new PredictionScheduleOptions
    {
        StateDirectory = path,
    }));

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"prediction-schedule-{Guid.NewGuid():N}");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}

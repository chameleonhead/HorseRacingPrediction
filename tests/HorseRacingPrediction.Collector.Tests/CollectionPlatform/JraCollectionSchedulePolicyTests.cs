using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraCollectionSchedulePolicyTests
{
    private readonly JraCollectionSchedulePolicy _policy = new();

    [TestMethod]
    public void Odds_CloseToRace_IsCriticalAndRepeatsEveryMinute()
    {
        var now = new DateTimeOffset(2026, 9, 12, 15, 25, 0, TimeSpan.FromHours(9));
        var result = _policy.Evaluate(new(ResourceType.RaceOdds, "JRA", "20260912-TOKYO-11"),
            State(ResourceType.RaceOdds), now);

        Assert.IsTrue(result.ShouldCollect);
        Assert.AreEqual(CollectionPriority.Critical, result.Priority);
        Assert.AreEqual(CollectionLane.Realtime, result.Lane);
        Assert.AreEqual(now.AddMinutes(1), result.NextCollectionAt);
    }

    [TestMethod]
    public void CurrentResult_DoesNotRepeat()
    {
        var state = State(ResourceType.RaceResult) with { Status = CollectionStateStatus.Current };
        var result = _policy.Evaluate(new(ResourceType.RaceResult, "JRA", "20260912-TOKYO-11"), state,
            new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.FromHours(9)));

        Assert.IsFalse(result.ShouldCollect);
        Assert.IsNull(result.NextCollectionAt);
    }

    [TestMethod]
    public void RaceWeekCard_IsRealtimeHighPriority()
    {
        var now = new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.FromHours(9));
        var result = _policy.Evaluate(new(ResourceType.RaceCard, "JRA", "20260912-TOKYO-11"),
            State(ResourceType.RaceCard), now);

        Assert.IsTrue(result.ShouldCollect);
        Assert.AreEqual(CollectionPriority.High, result.Priority);
        Assert.AreEqual(CollectionLane.Realtime, result.Lane);
    }

    private static CollectionStateSnapshot State(ResourceType type) => new(new(type, "JRA", "id"),
        new("test"), 1, 1, null, null, CollectionStateStatus.Pending);
}

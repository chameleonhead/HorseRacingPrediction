using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class AgentJobKeyFactoryTests
{
    [TestMethod]
    public void BuildRaceCardCollectionKey_FormatsExpectedKey()
    {
        var key = AgentJobKeyFactory.BuildRaceCardCollectionKey("JRA", new DateOnly(2026, 5, 16));

        Assert.AreEqual("JRA:race-card:2026-05-16", key);
    }

    [TestMethod]
    public void BuildRaceResultCollectionKey_FormatsExpectedKey()
    {
        var key = AgentJobKeyFactory.BuildRaceResultCollectionKey("JRA", new DateOnly(2026, 5, 16));

        Assert.AreEqual("JRA:race-result:2026-05-16", key);
    }
}
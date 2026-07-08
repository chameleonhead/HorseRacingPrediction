using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Scraping.Workflow;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class AgentWorkModeResolverTests
{
    [TestMethod]
    public void Resolve_TodayHasRace_ReturnsLive()
    {
        var today = new DateOnly(2026, 5, 16);
        var schedule = new JraRaceScheduleCollectionResult(
            today,
            [today],
            [today],
            null);

        var result = AgentWorkModeResolver.Resolve(today, schedule, preRaceLeadDays: 1);

        Assert.AreEqual(AgentWorkMode.Live, result);
    }

    [TestMethod]
    public void Resolve_UpcomingRaceWithinLeadDays_ReturnsPreRace()
    {
        var today = new DateOnly(2026, 5, 16);
        var schedule = new JraRaceScheduleCollectionResult(
            today,
            [today.AddDays(1)],
            [today.AddDays(1)],
            null);

        var result = AgentWorkModeResolver.Resolve(today, schedule, preRaceLeadDays: 1);

        Assert.AreEqual(AgentWorkMode.PreRace, result);
    }

    [TestMethod]
    public void Resolve_NoUpcomingRaceInWindow_ReturnsIdle()
    {
        var today = new DateOnly(2026, 5, 16);
        var schedule = new JraRaceScheduleCollectionResult(
            today,
            [today.AddDays(5)],
            [today.AddDays(5)],
            null);

        var result = AgentWorkModeResolver.Resolve(today, schedule, preRaceLeadDays: 1);

        Assert.AreEqual(AgentWorkMode.Idle, result);
    }
}
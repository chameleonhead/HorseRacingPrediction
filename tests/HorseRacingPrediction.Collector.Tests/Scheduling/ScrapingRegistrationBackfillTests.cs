// JRAサイト再設計（docs/jra-scraping.md）により、対象の ScrapingRegistrationService は一時的に無効化されている。
#if false
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Scraping.Workflow;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class ScrapingRegistrationBackfillTests
{
    [TestMethod]
    public void BuildResultCollectionDates_LiveMode_SuppressesHistoricalBackfill()
    {
        var today = new DateOnly(2026, 5, 16);
        var schedule = new JraRaceScheduleCollectionResult(today, [today], [today], null);
        var options = new AgentProcessingOptions
        {
            ResultLookbackDays = 3,
            LiveResultLookbackDays = 0,
            ResultLookaheadDays = 0,
            SuppressHistoricalBackfillDuringLive = true
        };

        var result = ScrapingRegistrationService.BuildResultCollectionDates(
            today,
            schedule,
            AgentWorkMode.Live,
            options);

        CollectionAssert.AreEqual(new[] { today }, result.ToArray());
    }

    [TestMethod]
    public void BuildResultCollectionDates_IdleMode_UsesDefaultLookback()
    {
        var today = new DateOnly(2026, 5, 16);
        var options = new AgentProcessingOptions
        {
            ResultLookbackDays = 2,
            ResultLookaheadDays = 0
        };

        var result = ScrapingRegistrationService.BuildResultCollectionDates(
            today,
            schedule: null,
            AgentWorkMode.Idle,
            options);

        CollectionAssert.AreEqual(
            new[] { today.AddDays(-2), today.AddDays(-1), today },
            result.ToArray());
    }
}
#endif
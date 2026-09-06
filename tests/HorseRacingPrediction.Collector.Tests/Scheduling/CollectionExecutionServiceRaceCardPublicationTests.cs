using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class CollectionExecutionServiceRaceCardPublicationTests
{
    [TestMethod]
    public void EstimateRaceCardPublicationDate_ForSaturdayRace_ReturnsThursday()
    {
        // 2026-09-12 は土曜日。
        var raceDate = new DateOnly(2026, 9, 12);

        var publicationDate = CollectionExecutionService.EstimateRaceCardPublicationDate(raceDate);

        Assert.AreEqual(new DateOnly(2026, 9, 10), publicationDate);
        Assert.AreEqual(DayOfWeek.Thursday, publicationDate.DayOfWeek);
    }

    [TestMethod]
    public void EstimateRaceCardPublicationDate_ForSundayRace_ReturnsThursday()
    {
        // 2026-09-13 は日曜日。
        var raceDate = new DateOnly(2026, 9, 13);

        var publicationDate = CollectionExecutionService.EstimateRaceCardPublicationDate(raceDate);

        Assert.AreEqual(new DateOnly(2026, 9, 10), publicationDate);
        Assert.AreEqual(DayOfWeek.Thursday, publicationDate.DayOfWeek);
    }

    [TestMethod]
    public void EstimateRaceCardPublicationDate_ForOtherWeekday_ReturnsTwoDaysBefore()
    {
        // 平日開催（重賞等）は情報がないため安全側に倒し、2日前とする。
        var raceDate = new DateOnly(2026, 9, 16); // 水曜日

        var publicationDate = CollectionExecutionService.EstimateRaceCardPublicationDate(raceDate);

        Assert.AreEqual(new DateOnly(2026, 9, 14), publicationDate);
    }
}

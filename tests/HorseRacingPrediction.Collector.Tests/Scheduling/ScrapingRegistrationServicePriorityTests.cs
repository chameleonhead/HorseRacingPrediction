using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

/// <summary>
/// <see cref="ScrapingRegistrationService"/> の優先度計算ロジックの単体テスト。
/// JRAのレースはほぼ土曜・日曜に集中するため、直近の週末は当日と同等の優先度で
/// 収集されることを検証する。
/// </summary>
[TestClass]
public sealed class ScrapingRegistrationServicePriorityTests
{
    // 2026-09-07は月曜日。
    private static readonly DateOnly Monday = new(2026, 9, 7);
    private static readonly DateOnly SameWeekSaturday = new(2026, 9, 12);
    private static readonly DateOnly SameWeekSunday = new(2026, 9, 13);
    private static readonly DateOnly NextWeekSaturday = new(2026, 9, 19);
    private static readonly DateOnly Tuesday = new(2026, 9, 8);

    [TestMethod]
    public void IsUpcomingWeekend_直近7日以内の土日はtrue()
    {
        Assert.IsTrue(ScrapingRegistrationService.IsUpcomingWeekend(SameWeekSaturday, Monday));
        Assert.IsTrue(ScrapingRegistrationService.IsUpcomingWeekend(SameWeekSunday, Monday));
    }

    [TestMethod]
    public void IsUpcomingWeekend_7日より先の土日はfalse()
    {
        Assert.IsFalse(ScrapingRegistrationService.IsUpcomingWeekend(NextWeekSaturday, Monday));
    }

    [TestMethod]
    public void IsUpcomingWeekend_平日はfalse()
    {
        Assert.IsFalse(ScrapingRegistrationService.IsUpcomingWeekend(Tuesday, Monday));
    }

    [TestMethod]
    public void IsUpcomingWeekend_過去日はfalse()
    {
        Assert.IsFalse(ScrapingRegistrationService.IsUpcomingWeekend(new DateOnly(2026, 9, 5), Monday));
    }

    [TestMethod]
    public void CalculateRaceCardPriority_直近の週末は当日と同じ優先度になる()
    {
        Assert.AreEqual(200, ScrapingRegistrationService.CalculateRaceCardPriority(Monday, Monday));
        Assert.AreEqual(200, ScrapingRegistrationService.CalculateRaceCardPriority(SameWeekSaturday, Monday));
        Assert.AreEqual(200, ScrapingRegistrationService.CalculateRaceCardPriority(SameWeekSunday, Monday));
        Assert.AreEqual(180, ScrapingRegistrationService.CalculateRaceCardPriority(Tuesday, Monday));
        Assert.AreEqual(180, ScrapingRegistrationService.CalculateRaceCardPriority(NextWeekSaturday, Monday));
    }

    [TestMethod]
    public void CalculateRaceResultPriority_直近の週末は当日と同じ優先度になる()
    {
        Assert.AreEqual(190, ScrapingRegistrationService.CalculateRaceResultPriority(Monday, Monday));
        Assert.AreEqual(190, ScrapingRegistrationService.CalculateRaceResultPriority(SameWeekSaturday, Monday));
        Assert.AreEqual(170, ScrapingRegistrationService.CalculateRaceResultPriority(Tuesday, Monday));
    }
}

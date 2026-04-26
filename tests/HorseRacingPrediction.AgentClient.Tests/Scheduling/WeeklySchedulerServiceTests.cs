using HorseRacingPrediction.AgentClient.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

/// <summary>
/// <see cref="WeeklySchedulerService"/> のスケジュール計算ロジックをテストする。
/// </summary>
[TestClass]
public sealed class WeeklySchedulerServiceTests
{
    // ------------------------------------------------------------------ //
    // テスト用ヘルパー
    // ------------------------------------------------------------------ //

    private static WeeklySchedulerService CreateService(WeeklySchedulerOptions? options = null)
    {
        options ??= new WeeklySchedulerOptions
        {
            ThursdayDiscoveryHour = 8,
            ThursdayRefreshHour = 14,
            FridayRaceCardHour = 9,
            FridayPostPositionHour = 18,
            SaturdayRaceCardHour = 7,
            SaturdayResultsHour = 21,
            SundayRaceCardHour = 7,
            SundayResultsHour = 21,
            MondayResultsHour = 9
        };

        return new WeeklySchedulerService(
            weeklyWorkflow: null!,
            raceCardWorkflow: null!,
            raceResultWorkflow: null!,
            stateStore: null!,
            options: Options.Create(options),
            logger: NullLogger<WeeklySchedulerService>.Instance);
    }

    /// <summary>JST のオフセット付き DateTimeOffset を生成するヘルパー。</summary>
    private static DateTimeOffset Jst(int year, int month, int day, int hour, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));

    // ------------------------------------------------------------------ //
    // 基本スケジュール
    // ------------------------------------------------------------------ //

    [TestMethod]
    [Description("木曜早朝 → ThursdayDiscovery (当日 08:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnThursdayMorning_ReturnsThursdayDiscovery()
    {
        var service = CreateService();
        // 2025-04-24 (木) 07:00 JST
        var now = Jst(2025, 4, 24, 7, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.ThursdayDiscovery, phase);
        Assert.AreEqual(Jst(2025, 4, 24, 8), nextTime);
    }

    [TestMethod]
    [Description("木曜昼過ぎ → ThursdayRefresh (当日 14:00) が次のフェーズになる")]
    public void GetNextScheduledItem_AfterThursdayDiscovery_ReturnsThursdayRefresh()
    {
        var service = CreateService();
        // 2025-04-24 (木) 09:00 JST（Discoveryフェーズ完了後）
        var now = Jst(2025, 4, 24, 9, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.ThursdayRefresh, phase);
        Assert.AreEqual(Jst(2025, 4, 24, 14), nextTime);
    }

    [TestMethod]
    [Description("木曜深夜 → FridayRaceCard (翌日 09:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnThursdayLateNight_ReturnsFridayRaceCard()
    {
        var service = CreateService();
        // 2025-04-24 (木) 23:00 JST
        var now = Jst(2025, 4, 24, 23, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.FridayRaceCard, phase);
        Assert.AreEqual(Jst(2025, 4, 25, 9), nextTime);
    }

    [TestMethod]
    [Description("金曜朝 → FridayRaceCard (当日 09:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnFridayMorning_ReturnsFridayRaceCard()
    {
        var service = CreateService();
        // 2025-04-25 (金) 08:00 JST
        var now = Jst(2025, 4, 25, 8, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.FridayRaceCard, phase);
        Assert.AreEqual(Jst(2025, 4, 25, 9), nextTime);
    }

    [TestMethod]
    [Description("金曜昼 → FridayPostPosition (当日 18:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnFridayAfternoon_ReturnsFridayPostPosition()
    {
        var service = CreateService();
        // 2025-04-25 (金) 12:00 JST
        var now = Jst(2025, 4, 25, 12, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.FridayPostPosition, phase);
        Assert.AreEqual(Jst(2025, 4, 25, 18), nextTime);
    }

    [TestMethod]
    [Description("土曜朝 → SaturdayRaceCard (当日 07:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnSaturdayBeforeRaceCard_ReturnsSaturdayRaceCard()
    {
        var service = CreateService();
        // 2025-04-26 (土) 06:00 JST
        var now = Jst(2025, 4, 26, 6, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.SaturdayRaceCard, phase);
        Assert.AreEqual(Jst(2025, 4, 26, 7), nextTime);
    }

    [TestMethod]
    [Description("土曜夜 → SaturdayResults (当日 21:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnSaturdayEvening_ReturnsSaturdayResults()
    {
        var service = CreateService();
        // 2025-04-26 (土) 20:00 JST
        var now = Jst(2025, 4, 26, 20, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.SaturdayResults, phase);
        Assert.AreEqual(Jst(2025, 4, 26, 21), nextTime);
    }

    [TestMethod]
    [Description("日曜朝 → SundayRaceCard (当日 07:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnSundayMorning_ReturnsSundayRaceCard()
    {
        var service = CreateService();
        // 2025-04-27 (日) 06:00 JST
        var now = Jst(2025, 4, 27, 6, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.SundayRaceCard, phase);
        Assert.AreEqual(Jst(2025, 4, 27, 7), nextTime);
    }

    [TestMethod]
    [Description("日曜夜 → SundayResults (当日 21:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnSundayEvening_ReturnsSundayResults()
    {
        var service = CreateService();
        // 2025-04-27 (日) 20:00 JST
        var now = Jst(2025, 4, 27, 20, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.SundayResults, phase);
        Assert.AreEqual(Jst(2025, 4, 27, 21), nextTime);
    }

    [TestMethod]
    [Description("月曜早朝 → MondayResults (当日 09:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnMondayMorning_ReturnsMondayResults()
    {
        var service = CreateService();
        // 2025-04-28 (月) 08:00 JST
        var now = Jst(2025, 4, 28, 8, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.MondayResults, phase);
        Assert.AreEqual(Jst(2025, 4, 28, 9), nextTime);
    }

    [TestMethod]
    [Description("月曜深夜（全フェーズ完了後）→ 翌週 ThursdayDiscovery が次のフェーズになる")]
    public void GetNextScheduledItem_AfterMondayResults_ReturnsNextWeekThursdayDiscovery()
    {
        var service = CreateService();
        // 2025-04-28 (月) 23:00 JST
        var now = Jst(2025, 4, 28, 23, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.ThursdayDiscovery, phase);
        // 翌週木曜: 2025-05-01
        Assert.AreEqual(Jst(2025, 5, 1, 8), nextTime);
    }

    [TestMethod]
    [Description("火曜深夜 → 木曜の ThursdayDiscovery が次のフェーズになる")]
    public void GetNextScheduledItem_OnTuesdayNight_ReturnsThursdayDiscovery()
    {
        var service = CreateService();
        // 2025-04-29 (火) 22:00 JST
        var now = Jst(2025, 4, 29, 22, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.ThursdayDiscovery, phase);
        // 次の木曜: 2025-05-01
        Assert.AreEqual(Jst(2025, 5, 1, 8), nextTime);
    }

    // ------------------------------------------------------------------ //
    // SchedulePhase の順序確認
    // ------------------------------------------------------------------ //

    [TestMethod]
    [Description("ある週のフェーズが正しい順序で返される")]
    public void GetNextScheduledItem_PhasesAreInChronologicalOrder()
    {
        var service = CreateService();

        // 2025-04-23 (水) 12:00 から始めて 9 フェーズを順に確認
        // 水曜スタートにすることで、前週 MondayResults が既に終わっており
        // 次の ThursdayDiscovery が最初のフェーズになることを保証する
        var expectedPhases = new[]
        {
            WeeklySchedulerService.SchedulePhase.ThursdayDiscovery,
            WeeklySchedulerService.SchedulePhase.ThursdayRefresh,
            WeeklySchedulerService.SchedulePhase.FridayRaceCard,
            WeeklySchedulerService.SchedulePhase.FridayPostPosition,
            WeeklySchedulerService.SchedulePhase.SaturdayRaceCard,
            WeeklySchedulerService.SchedulePhase.SaturdayResults,
            WeeklySchedulerService.SchedulePhase.SundayRaceCard,
            WeeklySchedulerService.SchedulePhase.SundayResults,
            WeeklySchedulerService.SchedulePhase.MondayResults
        };

        var now = Jst(2025, 4, 23, 12, 0); // 水曜 12:00（前週の全フェーズ完了後）

        foreach (var expected in expectedPhases)
        {
            var (nextTime, phase) = service.GetNextScheduledItem(now);
            Assert.AreEqual(expected, phase, $"Expected {expected} but got {phase} at {now}");
            now = nextTime.AddMinutes(1); // フェーズ直後に進める
        }
    }
}

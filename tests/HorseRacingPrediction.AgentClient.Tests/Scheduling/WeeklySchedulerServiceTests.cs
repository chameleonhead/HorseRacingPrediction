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
            DiscoveryHour = 8,
            DataRefreshHour = 14,
            PreRaceCardHour = 9,
            PostPositionHour = 18,
            RaceDay1CardHour = 7,
            RaceDay1ResultsHour = 21,
            RaceDay2CardHour = 7,
            RaceDay2ResultsHour = 21,
            FinalResultsHour = 9
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
    // 基本スケジュール（第1開催日 = 土曜、デフォルト設定）
    // ------------------------------------------------------------------ //

    [TestMethod]
    [Description("開催2日前（木曜）早朝 → Discovery (当日 08:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnThursdayMorning_ReturnsDiscovery()
    {
        var service = CreateService();
        // 2025-04-24 (木) 07:00 JST
        var now = Jst(2025, 4, 24, 7, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.Discovery, phase);
        Assert.AreEqual(Jst(2025, 4, 24, 8), nextTime);
    }

    [TestMethod]
    [Description("開催2日前（木曜）昼前 → DataRefresh (当日 14:00) が次のフェーズになる")]
    public void GetNextScheduledItem_AfterDiscovery_ReturnsDataRefresh()
    {
        var service = CreateService();
        // 2025-04-24 (木) 09:00 JST（Discoveryフェーズ完了後）
        var now = Jst(2025, 4, 24, 9, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.DataRefresh, phase);
        Assert.AreEqual(Jst(2025, 4, 24, 14), nextTime);
    }

    [TestMethod]
    [Description("開催2日前（木曜）深夜 → PreRaceCard (翌日 09:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnThursdayLateNight_ReturnsPreRaceCard()
    {
        var service = CreateService();
        // 2025-04-24 (木) 23:00 JST
        var now = Jst(2025, 4, 24, 23, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.PreRaceCard, phase);
        Assert.AreEqual(Jst(2025, 4, 25, 9), nextTime);
    }

    [TestMethod]
    [Description("開催前日（金曜）朝 → PreRaceCard (当日 09:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnFridayMorning_ReturnsPreRaceCard()
    {
        var service = CreateService();
        // 2025-04-25 (金) 08:00 JST
        var now = Jst(2025, 4, 25, 8, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.PreRaceCard, phase);
        Assert.AreEqual(Jst(2025, 4, 25, 9), nextTime);
    }

    [TestMethod]
    [Description("開催前日（金曜）昼 → PostPosition (当日 18:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnFridayAfternoon_ReturnsPostPosition()
    {
        var service = CreateService();
        // 2025-04-25 (金) 12:00 JST
        var now = Jst(2025, 4, 25, 12, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.PostPosition, phase);
        Assert.AreEqual(Jst(2025, 4, 25, 18), nextTime);
    }

    [TestMethod]
    [Description("第1開催日（土曜）朝 → RaceDay1Card (当日 07:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnSaturdayBeforeRaceCard_ReturnsRaceDay1Card()
    {
        var service = CreateService();
        // 2025-04-26 (土) 06:00 JST
        var now = Jst(2025, 4, 26, 6, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.RaceDay1Card, phase);
        Assert.AreEqual(Jst(2025, 4, 26, 7), nextTime);
    }

    [TestMethod]
    [Description("第1開催日（土曜）夜 → RaceDay1Results (当日 21:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnSaturdayEvening_ReturnsRaceDay1Results()
    {
        var service = CreateService();
        // 2025-04-26 (土) 20:00 JST
        var now = Jst(2025, 4, 26, 20, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.RaceDay1Results, phase);
        Assert.AreEqual(Jst(2025, 4, 26, 21), nextTime);
    }

    [TestMethod]
    [Description("第2開催日（日曜）朝 → RaceDay2Card (当日 07:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnSundayMorning_ReturnsRaceDay2Card()
    {
        var service = CreateService();
        // 2025-04-27 (日) 06:00 JST
        var now = Jst(2025, 4, 27, 6, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.RaceDay2Card, phase);
        Assert.AreEqual(Jst(2025, 4, 27, 7), nextTime);
    }

    [TestMethod]
    [Description("第2開催日（日曜）夜 → RaceDay2Results (当日 21:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnSundayEvening_ReturnsRaceDay2Results()
    {
        var service = CreateService();
        // 2025-04-27 (日) 20:00 JST
        var now = Jst(2025, 4, 27, 20, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.RaceDay2Results, phase);
        Assert.AreEqual(Jst(2025, 4, 27, 21), nextTime);
    }

    [TestMethod]
    [Description("最終成績収集日（月曜）早朝 → FinalResults (当日 09:00) が次のフェーズになる")]
    public void GetNextScheduledItem_OnMondayMorning_ReturnsFinalResults()
    {
        var service = CreateService();
        // 2025-04-28 (月) 08:00 JST
        var now = Jst(2025, 4, 28, 8, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.FinalResults, phase);
        Assert.AreEqual(Jst(2025, 4, 28, 9), nextTime);
    }

    [TestMethod]
    [Description("最終成績収集日（月曜）深夜（全フェーズ完了後）→ 翌週 Discovery が次のフェーズになる")]
    public void GetNextScheduledItem_AfterFinalResults_ReturnsNextCycleDiscovery()
    {
        var service = CreateService();
        // 2025-04-28 (月) 23:00 JST
        var now = Jst(2025, 4, 28, 23, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.Discovery, phase);
        // 翌週木曜: 2025-05-01
        Assert.AreEqual(Jst(2025, 5, 1, 8), nextTime);
    }

    [TestMethod]
    [Description("火曜深夜 → 木曜の Discovery が次のフェーズになる")]
    public void GetNextScheduledItem_OnTuesdayNight_ReturnsDiscovery()
    {
        var service = CreateService();
        // 2025-04-29 (火) 22:00 JST
        var now = Jst(2025, 4, 29, 22, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.Discovery, phase);
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
        // 水曜スタートにすることで、前週 FinalResults が既に終わっており
        // 次の Discovery が最初のフェーズになることを保証する
        var expectedPhases = new[]
        {
            WeeklySchedulerService.SchedulePhase.Discovery,
            WeeklySchedulerService.SchedulePhase.DataRefresh,
            WeeklySchedulerService.SchedulePhase.PreRaceCard,
            WeeklySchedulerService.SchedulePhase.PostPosition,
            WeeklySchedulerService.SchedulePhase.RaceDay1Card,
            WeeklySchedulerService.SchedulePhase.RaceDay1Results,
            WeeklySchedulerService.SchedulePhase.RaceDay2Card,
            WeeklySchedulerService.SchedulePhase.RaceDay2Results,
            WeeklySchedulerService.SchedulePhase.FinalResults
        };

        var now = Jst(2025, 4, 23, 12, 0); // 水曜 12:00（前週の全フェーズ完了後）

        foreach (var expected in expectedPhases)
        {
            var (nextTime, phase) = service.GetNextScheduledItem(now);
            Assert.AreEqual(expected, phase, $"Expected {expected} but got {phase} at {now}");
            now = nextTime.AddMinutes(1); // フェーズ直後に進める
        }
    }

    // ------------------------------------------------------------------ //
    // 任意曜日開催（非土曜開催）のテスト
    // ------------------------------------------------------------------ //

    [TestMethod]
    [Description("FirstRaceDayOfWeek=2（火曜）: 日曜朝 → Discovery (当日 08:00) が次のフェーズになる")]
    public void GetNextScheduledItem_TuesdayRaceDay_OnSundayMorning_ReturnsDiscovery()
    {
        // 中山金杯のような火曜開催を想定
        var options = new WeeklySchedulerOptions
        {
            FirstRaceDayOfWeek = (int)DayOfWeek.Tuesday, // 火曜 = 2
            DiscoveryHour = 8,
            DataRefreshHour = 14,
            PreRaceCardHour = 9,
            PostPositionHour = 18,
            RaceDay1CardHour = 7,
            RaceDay1ResultsHour = 21,
            RaceDay2CardHour = 7,
            RaceDay2ResultsHour = 21,
            FinalResultsHour = 9
        };
        var service = CreateService(options);

        // 2025-04-27 (日) 07:00 JST → 火曜の 2 日前（日曜）の発見フェーズが次
        var now = Jst(2025, 4, 27, 7, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.Discovery, phase);
        Assert.AreEqual(Jst(2025, 4, 27, 8), nextTime);
    }

    [TestMethod]
    [Description("FirstRaceDayOfWeek=2（火曜）: 火曜朝 → RaceDay1Card が次のフェーズになる")]
    public void GetNextScheduledItem_TuesdayRaceDay_OnTuesdayMorning_ReturnsRaceDay1Card()
    {
        var options = new WeeklySchedulerOptions
        {
            FirstRaceDayOfWeek = (int)DayOfWeek.Tuesday,
            DiscoveryHour = 8,
            DataRefreshHour = 14,
            PreRaceCardHour = 9,
            PostPositionHour = 18,
            RaceDay1CardHour = 7,
            RaceDay1ResultsHour = 21,
            RaceDay2CardHour = 7,
            RaceDay2ResultsHour = 21,
            FinalResultsHour = 9
        };
        var service = CreateService(options);

        // 2025-04-29 (火) 06:00 JST
        var now = Jst(2025, 4, 29, 6, 0);

        var (nextTime, phase) = service.GetNextScheduledItem(now);

        Assert.AreEqual(WeeklySchedulerService.SchedulePhase.RaceDay1Card, phase);
        Assert.AreEqual(Jst(2025, 4, 29, 7), nextTime);
    }

    [TestMethod]
    [Description("FirstRaceDayOfWeek=2（火曜）: フェーズが正しい順序で返される")]
    public void GetNextScheduledItem_TuesdayRaceDay_PhasesAreInChronologicalOrder()
    {
        var options = new WeeklySchedulerOptions
        {
            FirstRaceDayOfWeek = (int)DayOfWeek.Tuesday, // 火曜 = 2
            DiscoveryHour = 8,
            DataRefreshHour = 14,
            PreRaceCardHour = 9,
            PostPositionHour = 18,
            RaceDay1CardHour = 7,
            RaceDay1ResultsHour = 21,
            RaceDay2CardHour = 7,
            RaceDay2ResultsHour = 21,
            FinalResultsHour = 9
        };
        var service = CreateService(options);

        var expectedPhases = new[]
        {
            WeeklySchedulerService.SchedulePhase.Discovery,    // 日曜 08:00
            WeeklySchedulerService.SchedulePhase.DataRefresh,  // 日曜 14:00
            WeeklySchedulerService.SchedulePhase.PreRaceCard,  // 月曜 09:00
            WeeklySchedulerService.SchedulePhase.PostPosition, // 月曜 18:00
            WeeklySchedulerService.SchedulePhase.RaceDay1Card,    // 火曜 07:00
            WeeklySchedulerService.SchedulePhase.RaceDay1Results, // 火曜 21:00
            WeeklySchedulerService.SchedulePhase.RaceDay2Card,    // 水曜 07:00
            WeeklySchedulerService.SchedulePhase.RaceDay2Results, // 水曜 21:00
            WeeklySchedulerService.SchedulePhase.FinalResults     // 木曜 09:00
        };

        // 2025-04-26 (土) 12:00 スタート（前週の全フェーズ完了後、次の日曜 Discovery より前）
        var now = Jst(2025, 4, 26, 12, 0);

        foreach (var expected in expectedPhases)
        {
            var (nextTime, phase) = service.GetNextScheduledItem(now);
            Assert.AreEqual(expected, phase, $"Expected {expected} but got {phase} at {now}");
            now = nextTime.AddMinutes(1);
        }
    }
}

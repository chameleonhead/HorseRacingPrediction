using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

/// <summary>
/// Task29: 新Jra/Workflow層(<see cref="IJraScheduleCollectionWorkflow"/>/
/// <see cref="IJraRaceCardCollectionWorkflow"/>/<see cref="IJraRaceResultCollectionWorkflow"/>)を
/// 実際のJRAサイトに対して疎通確認するE2Eテスト。<see cref="JraSiteE2ETests"/> がNavigator/Page層を
/// 検証するのに対し、こちらはCollector本番と同じWorkflow層のオーケストレーションを検証する。
/// 実際の書き込みAPI/DBには依存しないよう、<see cref="ApiClient.IDataCollectionWriteService"/> は
/// <see cref="FakeDataCollectionWriteService"/>（インメモリ記録のみ）に差し替える。
/// サイトの状態（開催日程・実施中のレース等）に依存するため、通常の `dotnet test` では実行しない。
/// 実行するには:
///   dotnet test tests/HorseRacingPrediction.Scraping.Tests --filter TestCategory=External
/// </summary>
[TestClass]
[TestCategory("External")]
public sealed class JraWorkflowSiteE2ETests
{
    // RaceCardWorkflowは1開催日・1競馬場の全レース(最大12R)を順に収集し、
    // 既存のJraNavigator.ToRaceCardAsyncはレースごとに開催選択ページへの遷移からやり直す
    // 実装のため、1レースずつ検証するJraSiteE2ETestsよりかなり長めのタイムアウトを確保する。
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(20);

    private IWebBrowser _browser = null!;
    private JraSession _session = null!;
    private FakeDataCollectionWriteService _writeService = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _browser = await PlaywrightWebBrowser.CreateAsync();

        var pageReader = new JraPageReader(
            _browser,
            [
                new CalendarPageParser(),
                new RaceListPageParser(),
                new RaceCardPageParser(),
                new RaceResultPageParser(),
            ]);

        var navigator = new JraNavigator(_browser, pageReader);

        _session = new JraSession(_browser, navigator, pageReader);
        _writeService = new FakeDataCollectionWriteService();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_browser is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task RaceCardWorkflow_直近開催日の出馬表収集が成功する()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var (date, course) = await FindUpcomingOrTodayRaceDateAsync(cts.Token);

        var scheduleWorkflow = new JraScheduleCollectionWorkflow(_session);
        var courses = await scheduleWorkflow.CollectAsync(date, cts.Token);
        Assert.IsTrue(courses.Contains(course), $"{date:yyyy-MM-dd} の開催競馬場一覧に {course} が含まれていません。");

        var cardWorkflow = new JraRaceCardCollectionWorkflow(_session, _writeService);
        var result = await cardWorkflow.CollectAsync(date, course, cts.Token);

        Assert.AreEqual(date, result.Date);
        Assert.AreEqual(course, result.Course);
        Assert.IsTrue(result.Races.Count > 0, $"{date:yyyy-MM-dd} {course} のレースが1件も収集されませんでした。");
        Assert.IsTrue(result.RaceIds.Count > 0, "出馬表の保存に1件も成功しませんでした。");
        Assert.IsTrue(_writeService.UpsertRaceCalls.Count > 0, "UpsertRaceAsyncが1度も呼ばれませんでした。");
        Assert.IsTrue(_writeService.UpsertRaceEntryCalls.Count > 0, "UpsertRaceEntryAsyncが1度も呼ばれませんでした。");

        if (result.Errors.Count > 0)
        {
            Console.WriteLine($"[参考] 部分失敗あり: {string.Join(" / ", result.Errors)}");
        }
    }

    [TestMethod]
    public async Task RaceResultWorkflow_完了済みレースの成績収集が成功する()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var raceId = await FindCompletedRaceIdAsync(cts.Token);

        var resultWorkflow = new JraRaceResultCollectionWorkflow(_session, _writeService);
        var result = await resultWorkflow.CollectAsync(raceId, cts.Token);

        Assert.AreEqual(raceId, result.RaceId);
        Assert.IsTrue(result.SavedHorseNumbers.Count > 0, $"{raceId} の成績が1件も保存されませんでした。");
        Assert.IsTrue(_writeService.DeclareRaceEntryResultCalls.Count > 0, "DeclareRaceEntryResultAsyncが1度も呼ばれませんでした。");

        if (result.Errors.Count > 0)
        {
            Console.WriteLine($"[参考] 部分失敗あり: {string.Join(" / ", result.Errors)}");
        }
    }

    private async Task<(DateOnly Date, RaceCourse Course)> FindUpcomingOrTodayRaceDateAsync(
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var calendarPage = await _session.Navigate.ToCalendarAsync(
            new YearMonth(today.Year, today.Month),
            cancellationToken);

        Assert.IsInstanceOfType<JraCalendarPage>(calendarPage);
        var calendar = (JraCalendarPage)calendarPage;

        var target = calendar.RaceDates
            .Where(x => x.Date >= today)
            .OrderBy(x => x.Date)
            .FirstOrDefault()
            ?? calendar.RaceDates.OrderBy(x => x.Date).First();

        return (target.Date, target.Courses[0]);
    }

    /// <summary>
    /// <see cref="JraSiteE2ETests.完了済みRaceResult取得"/> と同じ方針: 今月/前月カレンダーから
    /// 今日より前の開催日を探し、1Rを対象レースとする(JRAのレースは必ず1Rが存在する)。
    /// </summary>
    private async Task<RaceId> FindCompletedRaceIdAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var currentMonthPage = await _session.Navigate.ToCalendarAsync(
            new YearMonth(today.Year, today.Month),
            cancellationToken);

        if (currentMonthPage is JraCalendarPage currentCalendar)
        {
            var pastInCurrentMonth = currentCalendar.RaceDates
                .Where(x => x.Date < today)
                .OrderByDescending(x => x.Date)
                .FirstOrDefault();

            if (pastInCurrentMonth is not null)
            {
                return new RaceId(pastInCurrentMonth.Date, pastInCurrentMonth.Courses[0], 1);
            }
        }

        var previousMonth = today.AddMonths(-1);
        var previousMonthPage = await _session.Navigate.ToCalendarAsync(
            new YearMonth(previousMonth.Year, previousMonth.Month),
            cancellationToken);

        Assert.IsInstanceOfType<JraCalendarPage>(previousMonthPage);
        var previousCalendar = (JraCalendarPage)previousMonthPage;

        var pastInPreviousMonth = previousCalendar.RaceDates
            .OrderByDescending(x => x.Date)
            .FirstOrDefault();

        Assert.IsNotNull(pastInPreviousMonth, "過去のレース開催日が見つかりませんでした。");

        return new RaceId(pastInPreviousMonth!.Date, pastInPreviousMonth.Courses[0], 1);
    }
}

using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

/// <summary>
/// 実際の JRA サイト (https://www.jra.go.jp/keiba/) に対して行う E2E テスト。
/// サイトの状態（開催日程・実施中のレース等）に依存するため、通常の
/// `dotnet test` では実行しない（TestCategory=External を除外して実行する）。
/// 実行するには:
///   dotnet test tests/HorseRacingPrediction.Scraping.Tests --filter TestCategory=External
///
/// Task 16（2026-09-04実施）の結果:
/// 現在月Calendar取得のみ実サイトに対して成功する。現在週RaceList取得/現在週RaceCard取得/
/// 完了済みRaceResult取得は、いずれも <see cref="JraNavigator"/> が
/// /keiba/calendar/ の開催日程ページに「日付+競馬場」のクリック可能リンクが存在する
/// ことを前提にしているのに対し、実際の同ページは静的なテキスト/表のみで遷移リンクを
/// 一切持たないため、JraNavigationException で失敗する（詳細は
/// <see cref="JraNavigator.IsCurrentRacePeriod"/> のコメントを参照）。
/// これは Task 1-15 の暫定実装がキャッチした、まさに Task 16 で見つけるべき
/// 前提の誤りである。正しい導線（出馬表/レース結果メニュー経由の
/// /JRADB/accessD.html・accessS.html 側のタブ/アコーディオン操作）の実装は
/// 本タスクの範囲を超えるため、ここでは意図的に修正せず、失敗する形のまま残している。
/// 同様の理由で「最近のRaceResult取得」「古いRaceResult取得」のテストケースは
/// 実サイト上の正しい導線を確認できなかったため実装していない
/// （<see cref="JraNavigator.IsRecentRacePeriod"/> のコメントを参照）。
/// </summary>
[TestClass]
[TestCategory("External")]
public sealed class JraSiteE2ETests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);

    private IWebBrowser _browser = null!;
    private JraSession _session = null!;

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

        _session = new JraSession(navigator, pageReader);
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
    public async Task 現在月Calendar取得()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var page = await _session.Navigate.ToCalendarAsync(
            new YearMonth(today.Year, today.Month),
            cts.Token);

        Assert.IsInstanceOfType<JraCalendarPage>(page);
        var calendar = (JraCalendarPage)page;

        Assert.AreEqual(new YearMonth(today.Year, today.Month), calendar.Month);
        Assert.IsTrue(calendar.RaceDates.Count > 0, "カレンダーに開催日が1件も存在しません。");

        foreach (var raceDate in calendar.RaceDates)
        {
            Assert.IsTrue(raceDate.Courses.Count > 0, $"{raceDate.Date:yyyy-MM-dd} に開催競馬場がありません。");
        }
    }

    [TestMethod]
    public async Task 現在週RaceList取得()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var calendarPage = await _session.Navigate.ToCalendarAsync(
            new YearMonth(today.Year, today.Month),
            cts.Token);

        Assert.IsInstanceOfType<JraCalendarPage>(calendarPage);
        var calendar = (JraCalendarPage)calendarPage;

        // 今日以降で最も近い開催日・競馬場を選ぶ（今日開催がなければ未来の開催日）。
        var target = calendar.RaceDates
            .Where(x => x.Date >= today)
            .OrderBy(x => x.Date)
            .FirstOrDefault()
            ?? calendar.RaceDates.OrderBy(x => x.Date).First();

        var course = target.Courses[0];

        var raceListPage = await _session.Navigate.ToRaceListAsync(
            target.Date,
            course,
            cts.Token);

        Assert.IsInstanceOfType<JraRaceListPage>(raceListPage);
        var raceList = (JraRaceListPage)raceListPage;

        Assert.AreEqual(target.Date, raceList.Date);
        Assert.AreEqual(course, raceList.Course);
        Assert.IsTrue(raceList.Races.Count > 0, $"{target.Date:yyyy-MM-dd} {course} のレース一覧が空でした。");
    }

    [TestMethod]
    public async Task 現在週RaceCard取得()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var calendarPage = await _session.Navigate.ToCalendarAsync(
            new YearMonth(today.Year, today.Month),
            cts.Token);

        Assert.IsInstanceOfType<JraCalendarPage>(calendarPage);
        var calendar = (JraCalendarPage)calendarPage;

        var target = calendar.RaceDates
            .Where(x => x.Date >= today)
            .OrderBy(x => x.Date)
            .FirstOrDefault()
            ?? calendar.RaceDates.OrderBy(x => x.Date).First();

        var course = target.Courses[0];

        var raceListPage = await _session.Navigate.ToRaceListAsync(
            target.Date,
            course,
            cts.Token);

        Assert.IsInstanceOfType<JraRaceListPage>(raceListPage);
        var raceList = (JraRaceListPage)raceListPage;
        Assert.IsTrue(raceList.Races.Count > 0, "レース一覧が空のため出馬表を取得できません。");

        var race = raceList.Races[0];

        var raceCardPage = await _session.Navigate.ToRaceCardAsync(
            race.Id,
            cts.Token);

        Assert.IsInstanceOfType<JraRaceCardPage>(raceCardPage);
        var raceCard = (JraRaceCardPage)raceCardPage;

        Assert.AreEqual(race.Id, raceCard.RaceId);
        Assert.IsTrue(raceCard.Entries.Count > 0, $"{race.Id} の出馬表に出走馬がありません。");
    }

    [TestMethod]
    public async Task 完了済みRaceResult取得()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var today = DateOnly.FromDateTime(DateTime.Today);

        // 今月のカレンダーから、今日より前の開催日（結果が確定している可能性が高い日）を探す。
        // 見つからなければ前月のカレンダーへフォールバックする。
        var (raceDate, course) = await FindPastRaceDateAsync(today, cts.Token);

        var raceListPage = await _session.Navigate.ToRaceListAsync(
            raceDate,
            course,
            cts.Token);

        Assert.IsInstanceOfType<JraRaceListPage>(raceListPage);
        var raceList = (JraRaceListPage)raceListPage;
        Assert.IsTrue(raceList.Races.Count > 0, $"{raceDate:yyyy-MM-dd} {course} のレース一覧が空でした。");

        var race = raceList.Races[0];

        var resultPage = await _session.Navigate.ToRaceResultAsync(
            race.Id,
            cts.Token);

        Assert.IsInstanceOfType<JraRaceResultPage>(resultPage);
        var result = (JraRaceResultPage)resultPage;

        Assert.AreEqual(race.Id, result.RaceId);
        Assert.IsTrue(result.Results.Count > 0, $"{race.Id} のレース結果が空でした。");
    }

    private async Task<(DateOnly Date, RaceCourse Course)> FindPastRaceDateAsync(
        DateOnly today,
        CancellationToken cancellationToken)
    {
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
                return (pastInCurrentMonth.Date, pastInCurrentMonth.Courses[0]);
            }
        }

        // 今月に過去開催がなければ前月を見る。
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

        return (pastInPreviousMonth!.Date, pastInPreviousMonth.Courses[0]);
    }
}

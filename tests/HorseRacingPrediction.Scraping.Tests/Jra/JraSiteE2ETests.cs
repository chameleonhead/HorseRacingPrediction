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
/// 実サイトを実際に操作して調査した結果、Task1-15が前提としていたナビゲーション構造は
/// 大きく実態と異なっていた。/keiba/calendar/ の開催日程ページはテーブルセルが
/// プレーンテキストのみでリンクを一切持たない（開催有無の確認専用）。実際にレース一覧へ
/// 到達する導線は、競馬メニューの「出馬表」「レース結果」（いずれもhrefを持たないJS要素で
/// ClickAsyncが必要）からクリック遷移する「開催選択」ページ上の、開催回・競馬場・開催日番号
/// を表すボタン（例：「4回中山1日」、これもhrefなし）である。また「過去のレース結果」は
/// クリック可能な要素ではなく、現在開催・直近開催（少なくとも8週間程度）は同一の
/// 「レース結果 開催選択」ページに同居している。<see cref="JraNavigator"/> をこの実際の
/// 構造に合わせて修正し、現在月Calendar取得・現在週RaceList取得・現在週RaceCard取得・
/// 完了済みRaceResult取得の4ケースは実サイトに対して成功することを確認した。
/// 「最近のRaceResult取得」は上記の通り「完了済みRaceResult取得」と同一の導線に帰着するため
/// 別テストケースとしては追加していない。「古いRaceResult取得」（過去レース結果検索フォーム
/// 経由、開催選択ページに載らない古い開催）は本タスクでは実サイトに対して検証できておらず、
/// 未実装のままとした（<see cref="JraNavigator.IsRecentRacePeriod"/> のコメントを参照）。
/// </summary>
[TestClass]
[TestCategory("External")]
public sealed class JraSiteE2ETests
{
    // 出馬表・レース結果ページは血統・複数走前成績まで含む大きなテーブルを持ち、
    // スナップショット抽出に時間がかかるため、他のケースより余裕を持たせている。
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(5);

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

        _session = new JraSession(_browser, navigator, pageReader);
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

        // Task16実サイト確認で判明: 「出馬表」導線（ToRaceListAsync）は
        // 現在開催週のみを対象とし、終了済みの開催日は開催選択ボタンとして
        // 列挙されない（「出馬表 開催選択」ページには当該週の日付しか表示されない）。
        // そのため過去日程のレース一覧取得には出馬表導線を使わず、JRAのレースは
        // 必ず1Rが存在するため、1Rを直接指定して「レース結果」導線
        // （ToRaceResultAsync が内部で使う「レース結果 開催選択」ページ）を検証する。
        var raceId = new RaceId(raceDate, course, 1);

        var resultPage = await _session.Navigate.ToRaceResultAsync(
            raceId,
            cts.Token);

        Assert.IsInstanceOfType<JraRaceResultPage>(resultPage);
        var result = (JraRaceResultPage)resultPage;

        Assert.AreEqual(raceId, result.RaceId);
        Assert.IsTrue(result.Results.Count > 0, $"{raceId} のレース結果が空でした。");
    }

    [TestMethod]
    public async Task 古いRaceResult取得()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Task 13 事前調査で確認済みの十分古いレース: 2020年5月3日 3回京都4日 11レース
        // （第161回天皇賞(春)）。現在から見て「現在開催」「直近開催」いずれの一覧ページにも
        // 載っていない期間であり、「過去レース結果検索」フォーム経由の遷移
        // （ToHistoricalRaceResultAsync）を通ることを検証する。
        var raceId = new RaceId(
            new DateOnly(2020, 5, 3),
            RaceCourse.Kyoto,
            11);

        var resultPage = await _session.Navigate.ToRaceResultAsync(
            raceId,
            cts.Token);

        Assert.IsInstanceOfType<JraRaceResultPage>(resultPage);
        var result = (JraRaceResultPage)resultPage;

        Assert.AreEqual(raceId, result.RaceId);
        Assert.IsTrue(result.Results.Count > 0, $"{raceId} のレース結果が空でした。");
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

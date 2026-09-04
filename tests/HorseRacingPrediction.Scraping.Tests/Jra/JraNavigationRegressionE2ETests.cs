using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

/// <summary>
/// 「過去月遷移バグ」修正（コミット b13ec72）で新設・変更した挙動を実際のJRAサイトに対して
/// 検証するE2Eテスト。テスト計画（依頼2マトリクス）で「新」と分類されたケースのうち、
/// 修正内容に直結する項目（2-2/2-3/2-4, 3-2, 5-4関連のNavigator層API, 1-3/1-5）を対象とする。
/// 実行するには:
///   dotnet test tests/HorseRacingPrediction.Scraping.Tests --filter TestCategory=External
/// </summary>
[TestClass]
[TestCategory("External")]
public sealed class JraNavigationRegressionE2ETests
{
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

    // 計画2-2/2-3: 出馬表専用の ToRaceListAsync（開催選択ページ /JRADB/accessD.html、
    // 今週～直近数週間程度しか掲載されない）に、月をまたいだ過去日を渡すケース。
    // 出馬表は公開期間を過ぎると通常ページ自体が消えるため、この導線では対応せず
    // 明確な理由付き例外（OutOfDisplayedRange）になることを確認する（意図した仕様）。
    // 成績収集はこの導線を使わず ToRaceResultListAsync を使うことが今回の修正の要点。
    [TestMethod]
    public async Task RaceList_過去2ヶ月の開催は出馬表導線では表示範囲外エラーになる()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var (date, course) = await FindPastMeetingAsync(monthsAgo: 2, cts.Token);

        var ex = await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => _session.Navigate.ToRaceListAsync(date, course, cts.Token));

        Assert.AreEqual(
            JraNavigationFailureReason.OutOfDisplayedRange,
            ex.Reason,
            $"想定外の例外理由でした: {ex.Message}");
    }

    // 計画5-4関連: 過去月遷移バグの根本原因だった「成績収集がToRaceListAsyncを使ってしまう」
    // 問題への修正として新設した ToRaceResultListAsync が、月をまたいだ過去日でも
    // Recent→Historicalフォールバックにより一覧取得に成功することを確認する。
    // JraSiteE2ETests の同種テストは ToRaceResultAsync（特定R）を検証済みだが、
    // こちらは一覧取得API自体（RaceResultCollectionジョブが実際に使うAPI）を検証する。
    [TestMethod]
    public async Task RaceResultListAsync_過去2ヶ月の開催の一覧取得がHistoricalへフォールバックして成功する()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var (date, course) = await FindPastMeetingAsync(monthsAgo: 2, cts.Token);

        var page = await _session.Navigate.ToRaceResultListAsync(date, course, cts.Token);

        Assert.IsTrue(
            page.Kind == JraPageKind.RaceList || page.Kind == JraPageKind.RaceResult,
            $"想定外のページ種別でした: {page.Kind}");

        if (page is JraRaceListPage raceList)
        {
            Assert.AreEqual(date, raceList.Date);
            Assert.AreEqual(course, raceList.Course);
            Assert.IsTrue(raceList.Races.Count > 0, $"{date:yyyy-MM-dd} {course} のレース一覧が空でした。");
        }
    }

    // 計画2-4: 開催のない日（土日以外の平日等）を出馬表導線に指定した場合の異常系。
    // カレンダー上に該当日の開催情報自体が存在しないため、「開催情報がありません」の
    // JraNavigationException になることを確認する。
    [TestMethod]
    public async Task RaceList_開催のない平日を指定すると開催情報なしの例外になる()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var calendarPage = await _session.Navigate.ToCalendarAsync(
            new YearMonth(today.Year, today.Month),
            cts.Token);

        Assert.IsInstanceOfType<JraCalendarPage>(calendarPage);
        var calendar = (JraCalendarPage)calendarPage;

        var raceDateSet = calendar.RaceDates.Select(x => x.Date).ToHashSet();

        var nonRaceDate = Enumerable.Range(1, DateTime.DaysInMonth(today.Year, today.Month))
            .Select(day => new DateOnly(today.Year, today.Month, day))
            .FirstOrDefault(d => !raceDateSet.Contains(d));

        if (nonRaceDate == default)
        {
            Assert.Inconclusive("今月中に開催のない日が見つからなかったため、このテストはスキップします。");
            return;
        }

        var ex = await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => _session.Navigate.ToRaceListAsync(nonRaceDate, RaceCourse.Tokyo, cts.Token));

        StringAssert.Contains(ex.Message, "開催情報がありません");
    }

    // 計画3-2: カレンダー上には開催予定があるが、出馬表がまだ公開されていない
    // 未来日（現在の許容幅である前後3日を超える先）を指定した場合、
    // NotYetPublished 理由の JraNavigationException になることを確認する。
    // カレンダー自体にそのような未来開催が見つからない場合はテストを不確定として扱う。
    [TestMethod]
    public async Task RaceList_数日以上先の未来開催は未公開エラーになる()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var today = DateOnly.FromDateTime(DateTime.Today);

        (DateOnly Date, RaceCourse Course)? target = null;

        // 今月・来月のカレンダーから、今日より5日以上先の開催日を探す
        // （Navigator内部の「現在開催週」しきい値は前後3日のため、これを確実に超える）。
        foreach (var monthOffset in new[] { 0, 1 })
        {
            var month = today.AddMonths(monthOffset);
            var calendarPage = await _session.Navigate.ToCalendarAsync(
                new YearMonth(month.Year, month.Month),
                cts.Token);

            if (calendarPage is not JraCalendarPage calendar)
            {
                continue;
            }

            var candidate = calendar.RaceDates
                .Where(x => x.Date.DayNumber - today.DayNumber >= 5)
                .OrderBy(x => x.Date)
                .FirstOrDefault();

            if (candidate is not null)
            {
                target = (candidate.Date, candidate.Courses[0]);
                break;
            }
        }

        if (target is null)
        {
            Assert.Inconclusive("5日以上先の開催日がカレンダーから見つからなかったため、このテストはスキップします。");
            return;
        }

        var ex = await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => _session.Navigate.ToRaceListAsync(target.Value.Date, target.Value.Course, cts.Token));

        Assert.AreEqual(
            JraNavigationFailureReason.NotYetPublished,
            ex.Reason,
            $"想定外の例外理由でした: {ex.Message}");
    }

    // 計画1-5: 前月・前々月への ToCalendarAsync 直接遷移（既存テストは
    // FindPastRaceDateAsync 経由の間接カバーのみで、2ヶ月以上前は未検証だった）。
    [TestMethod]
    public async Task Calendar_2ヶ月前のカレンダーを直接取得できる()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var target = DateOnly.FromDateTime(DateTime.Today).AddMonths(-2);
        var page = await _session.Navigate.ToCalendarAsync(
            new YearMonth(target.Year, target.Month),
            cts.Token);

        Assert.IsInstanceOfType<JraCalendarPage>(page);
        var calendar = (JraCalendarPage)page;

        Assert.AreEqual(new YearMonth(target.Year, target.Month), calendar.Month);
    }

    // 計画1-3: 翌月への ToCalendarAsync（月またぎのリンク遷移、SelectCalendarMonthAsyncの
    // リンク検索経路が未来方向でも機能することの確認）。
    [TestMethod]
    public async Task Calendar_翌月のカレンダーを直接取得できる()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var target = DateOnly.FromDateTime(DateTime.Today).AddMonths(1);
        var page = await _session.Navigate.ToCalendarAsync(
            new YearMonth(target.Year, target.Month),
            cts.Token);

        Assert.IsInstanceOfType<JraCalendarPage>(page);
        var calendar = (JraCalendarPage)page;

        Assert.AreEqual(new YearMonth(target.Year, target.Month), calendar.Month);
    }

    private async Task<(DateOnly Date, RaceCourse Course)> FindPastMeetingAsync(
        int monthsAgo,
        CancellationToken cancellationToken)
    {
        var targetMonth = DateOnly.FromDateTime(DateTime.Today).AddMonths(-monthsAgo);
        var calendarPage = await _session.Navigate.ToCalendarAsync(
            new YearMonth(targetMonth.Year, targetMonth.Month),
            cancellationToken);

        Assert.IsInstanceOfType<JraCalendarPage>(calendarPage);
        var calendar = (JraCalendarPage)calendarPage;

        Assert.IsTrue(calendar.RaceDates.Count > 0, $"{targetMonth:yyyy-MM} に開催日が見つかりませんでした。");
        var target = calendar.RaceDates.OrderByDescending(x => x.Date).First();

        return (target.Date, target.Courses[0]);
    }
}

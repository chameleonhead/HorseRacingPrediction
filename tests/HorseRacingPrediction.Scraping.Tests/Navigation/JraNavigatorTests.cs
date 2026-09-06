using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Navigation;

[TestClass]
public sealed class JraNavigatorTests
{
    private const string KeibaTopUrl = "https://www.jra.go.jp/keiba/";
    private const string CalendarUrl = "https://www.jra.go.jp/keiba/calendar/";

    private static PageSnapshot BuildCalendarSnapshot(
        string url,
        IEnumerable<PageLinkSnapshot> links)
    {
        var table = new PageTableSnapshot(
            Headers: [],
            Rows:
            [
                ["5 中山", "6", "7"],
            ]);

        var section = new PageSectionSnapshot(
            title: "開催日程",
            mainText: string.Empty,
            links: links.ToList(),
            actions: [],
            tables: [table],
            headings: ["開催日程>2026年9月"]);

        return new PageSnapshot(url, "開催日程", [section]);
    }

    private static PageSnapshot BuildRaceListSnapshot(string url)
    {
        var table = new PageTableSnapshot(
            Headers: ["R", "発走時刻", "レース名"],
            Rows: [["11R", "15:40", "テストステークス"]]);

        var section = new PageSectionSnapshot(
            title: "レース一覧",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new PageSnapshot(url, "2026年9月5日 中山 レース一覧", [section]);
    }

    private static PageSnapshot BuildRaceCardSnapshot(string url, string headingSuffix = "11R")
    {
        var table = new PageTableSnapshot(
            Headers: ["馬番", "馬名", "騎手"],
            Rows: [["1", "テストホース", "テスト騎手"]]);

        var section = new PageSectionSnapshot(
            title: "出馬表",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: [$"2026年9月5日 中山 {headingSuffix}"]);

        return new PageSnapshot(url, $"2026年9月5日 中山 {headingSuffix} 出馬表", [section]);
    }

    private static PageSnapshot BuildRaceResultSnapshot(string url, string headingSuffix = "11R")
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "3", "テストホース", "テスト騎手", "1:33.4"]]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            // 実サイトでは日付+レース番号を含む見出しの直後にレース名見出しが続く
            // (RaceResultPageParser.ParseRaceName参照)。
            headings: [$"2026年9月5日 中山 {headingSuffix}", "テストレース"]);

        return new PageSnapshot(url, $"2026年9月5日 中山 {headingSuffix} レース結果", [section]);
    }

    private static JraPageReader CreateReader(FakeWebBrowser browser)
        => new(browser,
        [
            new CalendarPageParser(),
            new RaceListPageParser(),
            new RaceCardPageParser(),
            new RaceResultPageParser(),
        ]);

    [TestMethod]
    public async Task ToKeibaTopAsync_NavigatesToKeibaTopUrl()
    {
        var browser = new FakeWebBrowser();
        var navigator = new JraNavigator(browser, CreateReader(browser));

        await navigator.ToKeibaTopAsync();

        CollectionAssert.Contains(browser.NavigatedUrls, KeibaTopUrl);
    }

    [TestMethod]
    public async Task ToCalendarAsync_LinkFound_ResolvesRelativeUrlAndNavigates()
    {
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(KeibaTopUrl);
        browser.SetLinks(KeibaTopUrl,
        [
            new PageLinkSnapshot("calendar/", "開催日程"),
        ]);
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToCalendarAsync(new YearMonth(2026, 9));

        CollectionAssert.Contains(browser.NavigatedUrls, CalendarUrl);
        Assert.AreEqual(JraPageKind.Calendar, page.Kind);
    }

    [TestMethod]
    public async Task ToCalendarAsync_LinkNotFound_FallsBackToDirectUrl()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        await navigator.ToCalendarAsync(new YearMonth(2026, 9));

        CollectionAssert.Contains(browser.NavigatedUrls, CalendarUrl);
    }

    private const string MeetingSelectionUrl = "https://www.jra.go.jp/JRADB/accessD.html";

    private static PageSnapshot BuildMeetingSelectionSnapshot(string url, string mainText)
    {
        var section = new PageSectionSnapshot(
            title: "開催選択",
            mainText: mainText,
            links: [],
            actions: [],
            tables: [],
            headings: ["開催選択"]);

        return new PageSnapshot(url, "開催選択", [section]);
    }

    [TestMethod]
    public async Task ToRaceListAsync_LinkFound_NavigatesAndReturnsRaceListPage()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/";

        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetCurrentUrl(CalendarUrl);
        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", raceListUrl);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToRaceListAsync(
            new DateOnly(2026, 9, 5),
            RaceCourse.Nakayama);

        Assert.AreEqual(JraPageKind.RaceList, page.Kind);
        var raceList = (JraRaceListPage)page;
        Assert.AreEqual(11, raceList.Races[0].Number);
    }

    [TestMethod]
    public async Task ToRaceListAsync_DateNotInCalendar_Throws()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceListAsync(
                new DateOnly(2026, 9, 6),
                RaceCourse.Nakayama));
    }

    [TestMethod]
    public async Task ToRaceListAsync_CourseNotOnDate_Throws()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceListAsync(
                new DateOnly(2026, 9, 5),
                RaceCourse.Hanshin));
    }

    [TestMethod]
    public async Task ToRaceListAsync_LinkNotFound_Throws()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, "9月5日 4回阪神1日"));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceListAsync(
                new DateOnly(2026, 9, 5),
                RaceCourse.Nakayama));
    }

    private static PageSnapshot BuildRaceListSnapshotWithCardLink(string url, string raceCardUrl)
    {
        var table = new PageTableSnapshot(
            Headers: ["R", "発走時刻", "レース名"],
            Rows: [["11R", "15:40", "テストステークス"]]);

        var section = new PageSectionSnapshot(
            title: "レース一覧",
            mainText: string.Empty,
            links: [new PageLinkSnapshot(raceCardUrl, "11R 出馬表")],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new PageSnapshot(url, "2026年9月5日 中山 レース一覧", [section]);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_UsesRaceListLinkToReachRaceCardPage()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/";
        const string raceCardUrl = "https://www.jra.go.jp/keiba/sample/racecard/11/";

        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", raceListUrl);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));
        browser.SetLinks(
            raceListUrl,
            [new PageLinkSnapshot(raceCardUrl, "11R 出馬表")]);
        browser.SetSnapshot(raceCardUrl, BuildRaceCardSnapshot(raceCardUrl));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var raceId = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11);

        var page = await navigator.ToRaceCardAsync(raceId);

        Assert.AreEqual(JraPageKind.RaceCard, page.Kind);
        CollectionAssert.Contains(browser.NavigatedUrls, raceCardUrl);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_RaceNotInList_Throws()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/";

        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", raceListUrl);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var raceId = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 5);

        await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceCardAsync(raceId));
    }

    private const string ResultSelectionUrl = "https://www.jra.go.jp/JRADB/accessS.html";

    [TestMethod]
    public async Task ToRaceResultAsync_CurrentPeriod_NavigatesViaRaceResultTopAndReturnsRaceResultPage()
    {
        const string raceResultUrl = "https://www.jra.go.jp/keiba/sample/result/0905/11/";

        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(KeibaTopUrl);
        browser.SetClickDestination("レース結果", ResultSelectionUrl);
        browser.SetSnapshot(
            ResultSelectionUrl,
            BuildMeetingSelectionSnapshot(ResultSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", raceResultUrl);
        browser.SetLinks(raceResultUrl, [new PageLinkSnapshot(raceResultUrl, "11レース")]);
        browser.SetSnapshot(raceResultUrl, BuildRaceResultSnapshot(raceResultUrl));

        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 5));

        var raceId = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11);

        var page = await navigator.ToRaceResultAsync(raceId);

        Assert.AreEqual(JraPageKind.RaceResult, page.Kind);
        CollectionAssert.Contains(browser.NavigatedUrls, raceResultUrl);
    }

    [TestMethod]
    public async Task ToRaceResultAsync_RecentPeriod_NavigatesViaRecentResultsLinkAndReturnsRaceResultPage()
    {
        const string raceResultUrl = "https://www.jra.go.jp/keiba/sample/result/recent/0905/11/";

        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(KeibaTopUrl);
        browser.SetClickDestination("レース結果", ResultSelectionUrl);
        // Task16実サイト確認で判明: 「過去のレース結果」は見出しでありクリック不可。
        // 現在開催・直近開催とも同一の開催選択ページに開催ボタンが並ぶため、
        // Currentと全く同じ遷移で到達できる。
        browser.SetSnapshot(
            ResultSelectionUrl,
            BuildMeetingSelectionSnapshot(ResultSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", raceResultUrl);
        browser.SetLinks(raceResultUrl, [new PageLinkSnapshot(raceResultUrl, "11レース")]);
        browser.SetSnapshot(raceResultUrl, BuildRaceResultSnapshot(raceResultUrl));

        // 現在から57日前 (現在開催週の範囲外・最近の過去開催の範囲内)。
        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 11, 1));

        var raceId = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11);

        var page = await navigator.ToRaceResultAsync(raceId);

        Assert.AreEqual(JraPageKind.RaceResult, page.Kind);
        CollectionAssert.Contains(browser.NavigatedUrls, raceResultUrl);
    }

    [TestMethod]
    public async Task ToRaceResultAsync_HistoricalPeriod_UsesSearchFormAndReturnsRaceResultPage()
    {
        const string searchUrl = "https://www.jra.go.jp/keiba/sample/search/";
        const string searchResultUrl = "https://www.jra.go.jp/keiba/sample/search/result/";
        const string raceResultUrl = "https://www.jra.go.jp/keiba/sample/search/result/11/";

        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(KeibaTopUrl);
        browser.SetClickDestination("レース結果", ResultSelectionUrl);
        browser.SetSnapshot(
            ResultSelectionUrl,
            BuildMeetingSelectionSnapshot(ResultSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("過去レース結果検索", searchUrl);
        browser.SetSubmitDestination(searchResultUrl);
        browser.SetSnapshot(
            searchResultUrl,
            BuildMeetingSelectionSnapshot(searchResultUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", raceResultUrl);
        browser.SetLinks(searchResultUrl, [new PageLinkSnapshot(raceResultUrl, "11R レース結果")]);
        browser.SetSnapshot(raceResultUrl, BuildRaceResultSnapshot(raceResultUrl));

        // 現在から遥か過去のレースであり、過去レース結果検索を利用する。
        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2027, 6, 1));

        var raceId = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11);

        var page = await navigator.ToRaceResultAsync(raceId);

        Assert.AreEqual(JraPageKind.RaceResult, page.Kind);
        CollectionAssert.Contains(browser.NavigatedUrls, raceResultUrl);
        Assert.IsTrue(browser.SelectOptionCalls.Count > 0);
    }

    private static PageSnapshot BuildRaceListSnapshotWithTwoRaces(string url)
    {
        var table = new PageTableSnapshot(
            Headers: ["R", "発走時刻", "レース名"],
            Rows:
            [
                ["11R", "15:40", "テストステークス"],
                ["12R", "15:10", "テストレース12"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース一覧",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new PageSnapshot(url, "2026年9月5日 中山 レース一覧", [section]);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_SameMeeting_SecondCallRepeatsFullNavigation()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/";
        const string raceCardUrl11 = "https://www.jra.go.jp/keiba/sample/racecard/11/";
        const string raceCardUrl12 = "https://www.jra.go.jp/keiba/sample/racecard/12/";

        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", raceListUrl);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshotWithTwoRaces(raceListUrl));
        browser.SetLinks(
            raceListUrl,
            [
                new PageLinkSnapshot(raceCardUrl11, "11R 出馬表"),
                new PageLinkSnapshot(raceCardUrl12, "12R 出馬表"),
            ]);
        browser.SetSnapshot(raceCardUrl11, BuildRaceCardSnapshot(raceCardUrl11, "11R"));
        browser.SetSnapshot(raceCardUrl12, BuildRaceCardSnapshot(raceCardUrl12, "12R"));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var date = new DateOnly(2026, 9, 5);
        var race11 = new RaceId(date, RaceCourse.Nakayama, 11);
        var race12 = new RaceId(date, RaceCourse.Nakayama, 12);

        var page1 = await navigator.ToRaceCardAsync(race11);
        var page2 = await navigator.ToRaceCardAsync(race12);

        Assert.AreEqual(JraPageKind.RaceCard, page1.Kind);
        Assert.AreEqual(JraPageKind.RaceCard, page2.Kind);
        Assert.AreEqual(raceCardUrl12, page2.Url);

        // JRAの開催選択・レース選択URL（CNAMEクエリパラメータ付き）は一度きりの
        // クリック遷移でのみ有効なセッション依存トークンであり、同じURLへ改めて
        // ナビゲート（再GET）するとエラーページに着地することが本番環境で判明した。
        // そのためGoBackやURLキャッシュには頼らず、2レース目もフルパス
        // （トップ→出馬表メニュー→開催選択ボタン）で毎回遷移する。
        Assert.AreEqual(
            2,
            browser.ClickedTexts.Count(x => x == "出馬表"),
            "「出馬表」メニューへのクリックは毎回行われるはず。");
        Assert.AreEqual(
            2,
            browser.ClickedTexts.Count(x => x == "4回中山1日"),
            "開催選択ボタンのクリックは毎回行われるはず。");
        Assert.AreEqual(0, browser.GoBackCallCount, "GoBackは使用しない。");
    }

    [TestMethod]
    public async Task ToRaceCardAsync_DifferentMeeting_FallsBackToFullNavigation()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/nakayama/";
        const string raceCardUrl11 = "https://www.jra.go.jp/keiba/sample/racecard/nakayama/11/";
        const string raceListUrlHanshin = "https://www.jra.go.jp/keiba/sample/racelist/hanshin/";
        const string raceCardUrlHanshin1 = "https://www.jra.go.jp/keiba/sample/racecard/hanshin/1/";
        const string meetingSelectionMainText =
            "9月5日 4回中山1日 9月6日 4回阪神1日";

        var browser = new FakeWebBrowser();

        // カレンダーには2日程（9/5 中山, 9/6 阪神）を用意する。
        var calendarTable = new PageTableSnapshot(
            Headers: [],
            Rows: [["5 中山", "6 阪神", "7"]]);
        var calendarSection = new PageSectionSnapshot(
            title: "開催日程",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [calendarTable],
            headings: ["開催日程>2026年9月"]);
        browser.SetSnapshot(CalendarUrl, new PageSnapshot(CalendarUrl, "開催日程", [calendarSection]));

        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, meetingSelectionMainText));

        browser.SetClickDestination("4回中山1日", raceListUrl);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));
        browser.SetLinks(raceListUrl, [new PageLinkSnapshot(raceCardUrl11, "11R 出馬表")]);
        browser.SetSnapshot(raceCardUrl11, BuildRaceCardSnapshot(raceCardUrl11, "11R"));

        browser.SetClickDestination("4回阪神1日", raceListUrlHanshin);
        var hanshinListTable = new PageTableSnapshot(
            Headers: ["R", "発走時刻", "レース名"],
            Rows: [["1R", "10:00", "テスト1レース"]]);
        var hanshinListSection = new PageSectionSnapshot(
            title: "レース一覧",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [hanshinListTable],
            headings: ["2026年9月6日 阪神"]);
        browser.SetSnapshot(
            raceListUrlHanshin,
            new PageSnapshot(raceListUrlHanshin, "2026年9月6日 阪神 レース一覧", [hanshinListSection]));
        browser.SetLinks(
            raceListUrlHanshin,
            [new PageLinkSnapshot(raceCardUrlHanshin1, "1R 出馬表")]);
        browser.SetSnapshot(raceCardUrlHanshin1, BuildRaceCardSnapshot(raceCardUrlHanshin1, "1R"));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var race11 = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11);
        var raceHanshin1 = new RaceId(new DateOnly(2026, 9, 6), RaceCourse.Hanshin, 1);

        var page1 = await navigator.ToRaceCardAsync(race11);
        var page2 = await navigator.ToRaceCardAsync(raceHanshin1);

        Assert.AreEqual(JraPageKind.RaceCard, page1.Kind);
        Assert.AreEqual(JraPageKind.RaceCard, page2.Kind);
        Assert.AreEqual(raceCardUrlHanshin1, page2.Url);

        // 開催が変わった場合はGoBackショートカットを試みず、毎回フルパスで
        // 「出馬表」メニュー・開催選択ボタンをクリックし直すはず。
        Assert.AreEqual(2, browser.ClickedTexts.Count(x => x == "出馬表"));
        Assert.AreEqual(0, browser.GoBackCallCount);
    }

    [TestMethod]
    public async Task ToRaceListAsync_MeetingButtonNotFound_FutureDate_ReasonIsNotYetPublished()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        // 開催選択ページに対象日(9/5)の見出しがまだ載っていない状態を模す。
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, string.Empty));

        // 対象日は「今日」より未来。
        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 1));

        var exception = await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceListAsync(
                new DateOnly(2026, 9, 5),
                RaceCourse.Nakayama));

        Assert.AreEqual(JraNavigationFailureReason.NotYetPublished, exception.Reason);
    }

    [TestMethod]
    public async Task ToRaceListAsync_MeetingButtonNotFound_PastDate_ReasonIsOutOfDisplayedRange()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, string.Empty));

        // 対象日は「今日」以前（過去月をまたいだ結果、開催選択ページの表示範囲外）。
        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 12, 1));

        var exception = await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceListAsync(
                new DateOnly(2026, 9, 5),
                RaceCourse.Nakayama));

        Assert.AreEqual(JraNavigationFailureReason.OutOfDisplayedRange, exception.Reason);
    }

    [TestMethod]
    public async Task ToRaceResultListAsync_CurrentPeriod_ReturnsRaceListPageWithoutSelectingRace()
    {
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(KeibaTopUrl);
        browser.SetClickDestination("レース結果", ResultSelectionUrl);
        browser.SetSnapshot(
            ResultSelectionUrl,
            BuildMeetingSelectionSnapshot(ResultSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", "https://www.jra.go.jp/keiba/sample/result/list/");
        browser.SetSnapshot(
            "https://www.jra.go.jp/keiba/sample/result/list/",
            BuildRaceListSnapshot("https://www.jra.go.jp/keiba/sample/result/list/"));

        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 5));

        var page = await navigator.ToRaceResultListAsync(
            new DateOnly(2026, 9, 5),
            RaceCourse.Nakayama);

        Assert.AreEqual(JraPageKind.RaceList, page.Kind);
        Assert.IsFalse(browser.ClickedTexts.Any(x => x.Contains("レース", StringComparison.Ordinal) && x != "レース結果"));
    }

    [TestMethod]
    public async Task ToRaceResultAsync_RecentPeriod_OutOfDisplayedRange_FallsBackToHistoricalRoute()
    {
        const string searchUrl = "https://www.jra.go.jp/keiba/sample/search/";
        const string searchResultUrl = "https://www.jra.go.jp/keiba/sample/search/result/";
        const string raceResultUrl = "https://www.jra.go.jp/keiba/sample/search/result/11/";

        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(KeibaTopUrl);
        browser.SetClickDestination("レース結果", ResultSelectionUrl);
        // 「レース結果 開催選択」ページには対象日(9/5)がまだ／もう載っていない
        // （実サイトの実際の掲載範囲がIsRecentRacePeriodの92日しきい値より狭いケースを模す）。
        browser.SetSnapshot(
            ResultSelectionUrl,
            BuildMeetingSelectionSnapshot(ResultSelectionUrl, "10月20日 4回中山1日"));

        browser.SetClickDestination("過去レース結果検索", searchUrl);
        browser.SetSubmitDestination(searchResultUrl);
        browser.SetSnapshot(
            searchResultUrl,
            BuildMeetingSelectionSnapshot(searchResultUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", raceResultUrl);
        browser.SetLinks(searchResultUrl, [new PageLinkSnapshot(raceResultUrl, "11R レース結果")]);
        browser.SetSnapshot(raceResultUrl, BuildRaceResultSnapshot(raceResultUrl));

        // 現在から57日前 (IsRecentRacePeriod=trueの範囲内だが、実際のページ掲載範囲は
        // それより狭いという状況を模している)。
        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 11, 1));

        var raceId = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11);

        var page = await navigator.ToRaceResultAsync(raceId);

        Assert.AreEqual(JraPageKind.RaceResult, page.Kind);
        CollectionAssert.Contains(browser.NavigatedUrls, raceResultUrl);
        Assert.IsTrue(browser.SelectOptionCalls.Count > 0, "Historicalルートへフォールバックし、年月選択が行われるはず。");
    }
}

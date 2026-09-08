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

    [TestMethod]
    [DataRow("/datafile/meikan/trainer.html", true)]
    [DataRow("/datafile/meikan/trainer.html?initial=a", true)]
    [DataRow("trainer.html", true)]
    [DataRow("https://www.jra.go.jp/datafile/meikan/trainer.html", true)]
    [DataRow("javascript:void(0)", false)]
    [DataRow("/datafile/meikan/jockey.html", false)]
    public void HasPath_HandlesRelativeAbsoluteAndPseudoActionUrls(string url, bool expected)
    {
        Assert.AreEqual(expected, JraNavigator.HasPath(url, "/datafile/meikan/trainer.html"));
    }

    private static TestPageSnapshot BuildCalendarSnapshot(
        string url,
        IEnumerable<TestPageLink> links)
    {
        var table = new TestPageTable(
            Headers: [],
            Rows:
            [
                ["5 中山", "6", "7"],
            ]);

        var section = new TestPageSection(
            title: "開催日程",
            mainText: string.Empty,
            links: links.ToList(),
            actions: [],
            tables: [table],
            headings: ["開催日程>2026年9月"]);

        return new TestPageSnapshot(url, "開催日程", [section]);
    }

    private static TestPageSnapshot BuildRaceListSnapshot(
        string url,
        IEnumerable<TestPageLink>? links = null)
    {
        var table = new TestPageTable(
            Headers: ["R", "発走時刻", "レース名"],
            Rows: [["11R", "15:40", "テストステークス"]]);

        var section = new TestPageSection(
            title: "レース一覧",
            mainText: string.Empty,
            links: links?.ToList() ?? [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new TestPageSnapshot(url, "2026年9月5日 中山 レース一覧", [section]);
    }

    private static TestPageSnapshot BuildRaceCardSnapshot(
        string url,
        string headingSuffix = "11R",
        string dateCourse = "2026年9月5日 中山",
        IEnumerable<TestPageLink>? links = null,
        IEnumerable<TestPageAction>? actions = null)
    {
        var table = new TestPageTable(
            Headers: ["馬番", "馬名", "騎手"],
            Rows: [["1", "テストホース", "テスト騎手"]]);

        var section = new TestPageSection(
            title: "出馬表",
            mainText: string.Empty,
            links: links?.ToList() ?? [],
            actions: actions?.ToList() ?? [],
            tables: [table],
            headings: [$"{dateCourse} {headingSuffix}"]);

        return new TestPageSnapshot(url, $"{dateCourse} {headingSuffix} 出馬表", [section]);
    }

    private static TestPageSnapshot BuildRaceResultSnapshot(string url, string headingSuffix = "11R")
    {
        var table = new TestPageTable(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows: [["1", "3", "テストホース", "テスト騎手", "1:33.4"]]);

        var section = new TestPageSection(
            title: "レース結果",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            // 実サイトでは日付+レース番号を含む見出しの直後にレース名見出しが続く
            // (RaceResultPageParser.ParseRaceName参照)。
            headings: [$"2026年9月5日 中山 {headingSuffix}", "テストレース"]);

        return new TestPageSnapshot(url, $"2026年9月5日 中山 {headingSuffix} レース結果", [section]);
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
            new TestPageLink("calendar/", "開催日程"),
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

    private static TestPageSnapshot BuildMeetingSelectionSnapshot(string url, string mainText)
    {
        var section = new TestPageSection(
            title: "開催選択",
            mainText: mainText,
            links: [],
            actions: [],
            tables: [],
            headings: ["開催選択"]);

        return new TestPageSnapshot(url, "開催選択", [section]);
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

    private static TestPageSnapshot BuildRaceListSnapshotWithCardLink(string url, string raceCardUrl)
    {
        var table = new TestPageTable(
            Headers: ["R", "発走時刻", "レース名"],
            Rows: [["11R", "15:40", "テストステークス"]]);

        var section = new TestPageSection(
            title: "レース一覧",
            mainText: string.Empty,
            links: [new TestPageLink(raceCardUrl, "11R 出馬表")],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new TestPageSnapshot(url, "2026年9月5日 中山 レース一覧", [section]);
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
            [new TestPageLink(raceCardUrl, "11R 出馬表")]);
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

    // 依頼書3.1節: RaceCardLookupPeriod（既定5日）に関するテスト。
    // 「今週開催」判定ではなく、古いレースについて出馬表探索自体を試みないための
    // 早期スキップであることを確認する。

    [TestMethod]
    public void IsWithinRaceCardLookupPeriod_WithinDefaultPeriod_ReturnsTrue()
    {
        var browser = new FakeWebBrowser();
        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 10));

        // 今日-5日ちょうど（境界）はまだ探索対象期間内。
        Assert.IsTrue(navigator.IsWithinRaceCardLookupPeriod(new DateOnly(2026, 9, 5)));
        // 未来日も当然対象期間内。
        Assert.IsTrue(navigator.IsWithinRaceCardLookupPeriod(new DateOnly(2026, 9, 20)));
    }

    [TestMethod]
    public void IsWithinRaceCardLookupPeriod_OlderThanDefaultPeriod_ReturnsFalse()
    {
        var browser = new FakeWebBrowser();
        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 10));

        // 今日-6日は探索対象期間外。
        Assert.IsFalse(navigator.IsWithinRaceCardLookupPeriod(new DateOnly(2026, 9, 4)));
    }

    [TestMethod]
    public void IsWithinRaceCardLookupPeriod_CustomPeriod_UsesConfiguredValue()
    {
        var browser = new FakeWebBrowser();

        // 期間は後から容易に変更できる（依頼書3.1節）ことを、コンストラクタ引数で
        // 差し替え可能な internal コンストラクタで確認する。
        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 10),
            raceCardLookupPeriodDays: 1);

        Assert.IsTrue(navigator.IsWithinRaceCardLookupPeriod(new DateOnly(2026, 9, 9)));
        Assert.IsFalse(navigator.IsWithinRaceCardLookupPeriod(new DateOnly(2026, 9, 8)));
    }

    [TestMethod]
    public async Task ToRaceCardAsync_DateOlderThanRaceCardLookupPeriod_SkipsWithoutNavigating()
    {
        var browser = new FakeWebBrowser();
        // カレンダー等、一切のスナップショットを設定しない。呼ばれた場合は
        // FakeWebBrowser側の未設定エラーで検出できる（早期スキップの確認）。

        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 10));

        // 今日(9/10)から6日前(9/4)はRaceCardLookupPeriod（既定5日）の対象外。
        var raceId = new RaceId(new DateOnly(2026, 9, 4), RaceCourse.Nakayama, 11);

        var ex = await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceCardAsync(raceId));

        Assert.AreEqual(JraNavigationFailureReason.OutOfDisplayedRange, ex.Reason);
        Assert.AreEqual(0, browser.NavigatedUrls.Count);
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
        browser.SetLinks(raceResultUrl, [new TestPageLink(raceResultUrl, "11レース")]);
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
        browser.SetLinks(raceResultUrl, [new TestPageLink(raceResultUrl, "11レース")]);
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
    public async Task ToRaceResultAsync_DirectLinkFoundOnCurrentPage_NavigatesDirectlyWithoutMeetingSelection()
    {
        const string currentRaceResultUrl = "https://www.jra.go.jp/keiba/sample/result/0905/1/";
        const string siblingRaceResultUrl = "https://www.jra.go.jp/keiba/sample/result/0905/2/";

        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(currentRaceResultUrl);

        // 現在ページ（1R結果ページ）に他レースへの直接リンクが存在するケース。
        browser.SetLinks(
            currentRaceResultUrl,
            [new TestPageLink(siblingRaceResultUrl, "2レース結果")]);
        browser.SetSnapshot(siblingRaceResultUrl, BuildRaceResultSnapshot(siblingRaceResultUrl, "2R"));

        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 5));

        var targetRace = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 2);

        var page = await navigator.ToRaceResultAsync(targetRace);

        Assert.AreEqual(JraPageKind.RaceResult, page.Kind);
        CollectionAssert.Contains(browser.NavigatedUrls, siblingRaceResultUrl);

        // 開催選択トップ（重い経路）へは一切遷移していないこと。
        CollectionAssert.DoesNotContain(browser.NavigatedUrls, ResultSelectionUrl);
    }

    [TestMethod]
    public async Task ToRaceResultAsync_NoDirectLinkOnCurrentPage_FallsBackToFullMeetingSelectionRoute()
    {
        const string currentRaceResultUrl = "https://www.jra.go.jp/keiba/sample/result/0905/1/";
        const string siblingRaceResultUrl = "https://www.jra.go.jp/keiba/sample/result/0905/2/";

        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(currentRaceResultUrl);

        // 現在ページに直接リンクは存在しない（links未設定＝空）。

        // フォールバック先のフルパス（開催選択経由）。
        browser.SetClickDestination("レース結果", ResultSelectionUrl);
        browser.SetSnapshot(
            ResultSelectionUrl,
            BuildMeetingSelectionSnapshot(ResultSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", siblingRaceResultUrl);
        browser.SetLinks(siblingRaceResultUrl, [new TestPageLink(siblingRaceResultUrl, "2レース")]);
        browser.SetSnapshot(siblingRaceResultUrl, BuildRaceResultSnapshot(siblingRaceResultUrl, "2R"));

        var navigator = new JraNavigator(
            browser,
            CreateReader(browser),
            logger: null,
            today: () => new DateOnly(2026, 9, 5));

        var targetRace = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 2);

        var page = await navigator.ToRaceResultAsync(targetRace);

        Assert.AreEqual(JraPageKind.RaceResult, page.Kind);
        CollectionAssert.Contains(browser.NavigatedUrls, siblingRaceResultUrl);
        CollectionAssert.Contains(browser.NavigatedUrls, ResultSelectionUrl);
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
        browser.SetLinks(searchResultUrl, [new TestPageLink(raceResultUrl, "11R レース結果")]);
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

    private static TestPageSnapshot BuildRaceListSnapshotWithTwoRaces(
        string url,
        IEnumerable<TestPageLink>? links = null)
    {
        var table = new TestPageTable(
            Headers: ["R", "発走時刻", "レース名"],
            Rows:
            [
                ["11R", "15:40", "テストステークス"],
                ["12R", "15:10", "テストレース12"],
            ]);

        var section = new TestPageSection(
            title: "レース一覧",
            mainText: string.Empty,
            links: links?.ToList() ?? [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new TestPageSnapshot(url, "2026年9月5日 中山 レース一覧", [section]);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_SameMeeting_SecondCallClicksRaceNumberOnly()
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
        var raceLinks = new[]
        {
            new TestPageLink(raceCardUrl11, "11R 出馬表"),
            new TestPageLink(raceCardUrl12, "12R 出馬表"),
        };
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshotWithTwoRaces(raceListUrl, raceLinks));
        browser.SetLinks(
            raceListUrl,
            raceLinks);
        browser.SetSnapshot(raceCardUrl11, BuildRaceCardSnapshot(
            raceCardUrl11,
            "11R",
            links: [new TestPageLink(raceCardUrl12, "12R")]));
        browser.SetSnapshot(raceCardUrl12, BuildRaceCardSnapshot(raceCardUrl12, "12R"));
        browser.SetClickDestination("12R", raceCardUrl12);

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var date = new DateOnly(2026, 9, 5);
        var race11 = new RaceId(date, RaceCourse.Nakayama, 11);
        var race12 = new RaceId(date, RaceCourse.Nakayama, 12);

        var page1 = await navigator.ToRaceCardAsync(race11);
        var page2 = await navigator.ToRaceCardAsync(race12);

        Assert.AreEqual(JraPageKind.RaceCard, page1.Kind);
        Assert.AreEqual(JraPageKind.RaceCard, page2.Kind);
        Assert.AreEqual(raceCardUrl12, page2.Url);

        Assert.AreEqual(
            1,
            browser.ClickedTexts.Count(x => x == "出馬表"),
            "2レース目ではフルパスへ戻らないはず。");
        Assert.AreEqual(
            1,
            browser.ClickedTexts.Count(x => x == "4回中山1日"),
            "2レース目では開催選択をやり直さないはず。");
        CollectionAssert.Contains(browser.ClickedTexts, "12R");
        Assert.AreEqual(0, browser.GoBackCallCount, "GoBackは使用しない。");
    }

    [TestMethod]
    public async Task ToRaceListAsync_CurrentTargetRaceList_PerformsNoNavigation()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/";
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(raceListUrl);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));
        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToRaceListAsync(
            new DateOnly(2026, 9, 5),
            RaceCourse.Nakayama);

        Assert.IsInstanceOfType<JraRaceListPage>(page);
        Assert.AreEqual(raceListUrl, page.Url);
        Assert.AreEqual(0, browser.NavigatedUrls.Count);
        Assert.AreEqual(0, browser.ClickedTexts.Count);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_CurrentTargetCard_PerformsNoNavigation()
    {
        const string url = "https://www.jra.go.jp/keiba/sample/racecard/11/";
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(url);
        browser.SetSnapshot(url, BuildRaceCardSnapshot(url));
        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToRaceCardAsync(
            new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11));

        Assert.AreEqual(url, page.Url);
        Assert.AreEqual(0, browser.NavigatedUrls.Count);
        Assert.AreEqual(0, browser.ClickedTexts.Count);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_CurrentRaceList_ClicksRaceNumberOnly()
    {
        const string listUrl = "https://www.jra.go.jp/keiba/sample/racelist/";
        const string cardUrl = "https://www.jra.go.jp/keiba/sample/racecard/11/";
        var link = new TestPageLink(cardUrl, "11R 出馬表");
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(listUrl);
        browser.SetSnapshot(listUrl, BuildRaceListSnapshot(listUrl, [link]));
        browser.SetClickDestination(link.Title, cardUrl);
        browser.SetSnapshot(cardUrl, BuildRaceCardSnapshot(cardUrl));
        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToRaceCardAsync(
            new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11));

        Assert.AreEqual(cardUrl, page.Url);
        CollectionAssert.AreEqual(new[] { link.Title }, browser.ClickedTexts);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_SameDateDifferentCourse_ClicksCourseThenRaceNumber()
    {
        const string currentUrl = "https://www.jra.go.jp/keiba/sample/nakayama/11/";
        const string switchedUrl = "https://www.jra.go.jp/keiba/sample/hanshin/11/";
        const string targetUrl = "https://www.jra.go.jp/keiba/sample/hanshin/12/";
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(currentUrl);
        browser.SetSnapshot(currentUrl, BuildRaceCardSnapshot(
            currentUrl,
            actions: [new TestPageAction("阪神", "button")]));
        browser.SetClickDestination("阪神", switchedUrl);
        browser.SetSnapshot(switchedUrl, BuildRaceCardSnapshot(
            switchedUrl,
            "11R",
            "2026年9月5日 阪神",
            links: [new TestPageLink(targetUrl, "12R")]));
        browser.SetClickDestination("12R", targetUrl);
        browser.SetSnapshot(targetUrl, BuildRaceCardSnapshot(targetUrl, "12R", "2026年9月5日 阪神"));
        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToRaceCardAsync(
            new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Hanshin, 12));

        Assert.AreEqual(targetUrl, page.Url);
        CollectionAssert.AreEqual(new[] { "阪神", "12R" }, browser.ClickedTexts);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_SameCourseDifferentDate_ClicksDateThenRaceNumber()
    {
        const string currentUrl = "https://www.jra.go.jp/keiba/sample/0905/11/";
        const string switchedUrl = "https://www.jra.go.jp/keiba/sample/0906/list/";
        const string targetUrl = "https://www.jra.go.jp/keiba/sample/0906/12/";
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(currentUrl);
        browser.SetSnapshot(currentUrl, BuildRaceCardSnapshot(
            currentUrl,
            links: [new TestPageLink(switchedUrl, "9月6日（日曜）")]));
        browser.SetClickDestination("9月6日（日曜）", switchedUrl);
        var targetLink = new TestPageLink(targetUrl, "12R 出馬表");
        browser.SetSnapshot(switchedUrl, new TestPageSnapshot(
            switchedUrl,
            "2026年9月6日 中山 レース一覧",
            [new TestPageSection("レース一覧", string.Empty, [targetLink], [],
                [new TestPageTable(["R", "発走時刻", "レース名"], [["12R", "16:00", "テスト"]])],
                ["2026年9月6日 中山"])]));
        browser.SetClickDestination(targetLink.Title, targetUrl);
        browser.SetSnapshot(targetUrl, BuildRaceCardSnapshot(targetUrl, "12R", "2026年9月6日 中山"));
        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToRaceCardAsync(
            new RaceId(new DateOnly(2026, 9, 6), RaceCourse.Nakayama, 12));

        Assert.AreEqual(targetUrl, page.Url);
        CollectionAssert.AreEqual(new[] { "9月6日（日曜）", "12R 出馬表" }, browser.ClickedTexts);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_ShortcutReachesWrongRace_FallsBackToFullNavigation()
    {
        const string currentUrl = "https://www.jra.go.jp/keiba/sample/racecard/11/";
        const string wrongUrl = "https://www.jra.go.jp/keiba/sample/racecard/wrong/";
        const string listUrl = "https://www.jra.go.jp/keiba/sample/racelist/";
        const string targetUrl = "https://www.jra.go.jp/keiba/sample/racecard/12/";
        var targetLink = new TestPageLink(targetUrl, "12R 出馬表");
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(currentUrl);
        browser.SetSnapshot(currentUrl, BuildRaceCardSnapshot(
            currentUrl,
            links: [new TestPageLink(wrongUrl, "12R")]));
        browser.SetClickDestination("12R", wrongUrl);
        browser.SetSnapshot(wrongUrl, BuildRaceCardSnapshot(wrongUrl, "10R"));
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, "9月5日 4回中山1日"));
        browser.SetClickDestination("4回中山1日", listUrl);
        browser.SetSnapshot(listUrl, BuildRaceListSnapshotWithTwoRaces(listUrl));
        browser.SetLinks(listUrl, [targetLink]);
        browser.SetSnapshot(targetUrl, BuildRaceCardSnapshot(targetUrl, "12R"));
        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToRaceCardAsync(
            new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 12));

        Assert.AreEqual(targetUrl, page.Url);
        CollectionAssert.Contains(browser.ClickedTexts, "12R");
        CollectionAssert.Contains(browser.ClickedTexts, "出馬表");
        CollectionAssert.Contains(browser.NavigatedUrls, targetUrl);
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
        var calendarTable = new TestPageTable(
            Headers: [],
            Rows: [["5 中山", "6 阪神", "7"]]);
        var calendarSection = new TestPageSection(
            title: "開催日程",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [calendarTable],
            headings: ["開催日程>2026年9月"]);
        browser.SetSnapshot(CalendarUrl, new TestPageSnapshot(CalendarUrl, "開催日程", [calendarSection]));

        browser.SetClickDestination("出馬表", MeetingSelectionUrl);
        browser.SetSnapshot(
            MeetingSelectionUrl,
            BuildMeetingSelectionSnapshot(MeetingSelectionUrl, meetingSelectionMainText));

        browser.SetClickDestination("4回中山1日", raceListUrl);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));
        browser.SetLinks(raceListUrl, [new TestPageLink(raceCardUrl11, "11R 出馬表")]);
        browser.SetSnapshot(raceCardUrl11, BuildRaceCardSnapshot(raceCardUrl11, "11R"));

        browser.SetClickDestination("4回阪神1日", raceListUrlHanshin);
        var hanshinListTable = new TestPageTable(
            Headers: ["R", "発走時刻", "レース名"],
            Rows: [["1R", "10:00", "テスト1レース"]]);
        var hanshinListSection = new TestPageSection(
            title: "レース一覧",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [hanshinListTable],
            headings: ["2026年9月6日 阪神"]);
        browser.SetSnapshot(
            raceListUrlHanshin,
            new TestPageSnapshot(raceListUrlHanshin, "2026年9月6日 阪神 レース一覧", [hanshinListSection]));
        browser.SetLinks(
            raceListUrlHanshin,
            [new TestPageLink(raceCardUrlHanshin1, "1R 出馬表")]);
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
        browser.SetLinks(searchResultUrl, [new TestPageLink(raceResultUrl, "11R レース結果")]);
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

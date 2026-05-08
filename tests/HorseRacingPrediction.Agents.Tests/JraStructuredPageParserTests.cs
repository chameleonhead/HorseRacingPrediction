using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.JraAgent;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public class JraStructuredPageParserTests
{
    [TestMethod]
    public void Detect_KeibaMenuAndCalendarPageKinds()
    {
        var keibaMenu = new PageSnapshot(
            Url: "https://www.jra.go.jp/keiba/",
            Title: "競馬メニュー | JRA",
            MainText: "競馬メニュー 開催日程 出馬表 オッズ レース結果",
            Headings: ["競馬メニュー"],
            Links: [],
            Actions: [],
            Tables: []);

        var calendar = new PageSnapshot(
            Url: "https://www.jra.go.jp/keiba/calendar/may.html",
            Title: "開催日程 2026年5月 | JRA",
            MainText: "開催日程 2026年5月",
            Headings: ["開催日程", "5月"],
            Links: [],
            Actions: [],
            Tables: []);

        Assert.AreEqual(JraPageKind.KeibaMenu, JraPageKindDetector.Detect(keibaMenu.Url, keibaMenu));
        Assert.AreEqual(JraPageKind.ScheduleCalendar, JraPageKindDetector.Detect(calendar.Url, calendar));
    }

    [TestMethod]
    public void Detect_ThisWeekAndGradeOneSpecialPageKinds()
    {
        var thisWeek = new PageSnapshot(
            Url: "https://www.jra.go.jp/keiba/thisweek/",
            Title: "今週の注目レース | JRA",
            MainText: "今週の注目レース 5月9日～5月10日",
            Headings: ["今週の注目レース"],
            Links: [],
            Actions: [],
            Tables: []);

        var gradeOne = new PageSnapshot(
            Url: "https://www.jra.go.jp/keiba/g1/nmc.html",
            Title: "NHKマイルカップ | JRA",
            MainText: "GⅠレース 2026年5月10日（日曜） 東京競馬場 1600メートル（芝） 出馬表",
            Headings: ["NHKマイルカップ"],
            Links:
            [
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", "出馬表"),
            ],
            Actions: [],
            Tables: []);

        Assert.AreEqual(JraPageKind.ThisWeekFeature, JraPageKindDetector.Detect(thisWeek.Url, thisWeek));
        Assert.AreEqual(JraPageKind.GradeOneSpecial, JraPageKindDetector.Detect(gradeOne.Url, gradeOne));
    }

    [TestMethod]
    public void KeibaMenuParser_ExtractsPrimaryEntriesAndScheduleLink()
    {
        var snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/keiba/",
            Title: "競馬メニュー | JRA",
            MainText: "今週の開催情報",
            Headings: ["競馬メニュー", "今週の開催情報"],
            Links:
            [
                new SearchResultLink("https://www.jra.go.jp/keiba/calendar/", "開催日程"),
                new SearchResultLink("https://www.jra.go.jp/keiba/thisweek/", "今週の注目レース"),
                new SearchResultLink("https://www.jra.go.jp/keiba/baba/index.html", "馬場情報"),
            ],
            Actions:
            [
                new PageActionSnapshot("出馬表", "link"),
                new PageActionSnapshot("オッズ", "link"),
            ],
            Tables: []);

        var result = new JraKeibaMenuParser().Parse(snapshot);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual("開催日程", result.Data.ScheduleEntryText);
        Assert.IsTrue(result.RecommendedNextLinks.Any(link => link.Relation == JraStructuredLinkRelations.OpenSchedule));
    }

    [TestMethod]
    public void ScheduleCalendarParser_ExtractsRaceDatesAndRacecourses()
    {
        var snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/keiba/calendar/may.html",
            Title: "開催日程 2026年5月 | JRA",
            MainText: "開催日程 2026年5月 9 地方 東京 エプソムC(GIII) 京都 京都新聞杯(GII) 新潟 10 地方 東京 NHKマイルC(GI) 京都 新潟",
            Headings: ["開催日程", "5月"],
            Links:
            [
                new SearchResultLink("https://www.jra.go.jp/keiba/calendar/apr.html", "4月"),
                new SearchResultLink("https://www.jra.go.jp/keiba/calendar/may.html", "5月"),
                new SearchResultLink("https://www.jra.go.jp/keiba/calendar/jun.html", "6月"),
            ],
            Actions: [],
            Tables:
            [
                new PageTableSnapshot(
                    Headers: [],
                    Rows:
                    [
                        ["9 地方 東京 エプソムC(GIII) 京都 京都新聞杯(GII) 新潟"],
                        ["10 地方 東京 NHKマイルC(GI) 京都 新潟"],
                    ])
            ]);

        var result = new JraScheduleCalendarParser().Parse(snapshot);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual(2026, result.Data.Year);
        Assert.AreEqual(5, result.Data.Month);
        Assert.AreEqual(2, result.Data.ScheduledDays.Count);
        CollectionAssert.AreEquivalent(new[] { "東京", "京都", "新潟" }, result.Data.ScheduledDays[0].Racecourses.ToList());
    }

    [TestMethod]
    public void ThisWeekFeatureParser_ExtractsFeaturedRaceLinks()
    {
        var snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/keiba/thisweek/",
            Title: "今週の注目レース | JRA",
            MainText: "今週の注目レース 5月9日～5月10日",
            Headings: ["今週の注目レース"],
            Links:
            [
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc.html", "5月10日（日曜） NHKマイルカップ（GⅠ） 東京競馬場 芝1600メートル"),
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc.html", "レーストップ"),
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", "出馬表"),
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc/horse.html", "出走馬情報"),
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc/data.html", "データ分析"),
            ],
            Actions: [],
            Tables: []);

        var result = new JraThisWeekFeatureParser().Parse(snapshot);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual(1, result.Data.FeaturedRaces.Count);
        Assert.AreEqual("NHKマイルカップ", result.Data.FeaturedRaces[0].RaceName);
        Assert.AreEqual("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", result.Data.FeaturedRaces[0].RaceCardUrl);
    }

    [TestMethod]
    public void GradeOneSpecialParser_ExtractsMetadataAndTabs()
    {
        var snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/keiba/g1/nmc.html",
            Title: "NHKマイルカップ | JRA",
            MainText: "GⅠレース 2026年5月10日（日曜） 東京競馬場 芝1600メートル NHKマイルカップ 関連ニュース 第31回NHKマイルカップ（GⅠ）枠順確定",
            Headings: ["NHKマイルカップ"],
            Links:
            [
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", "出馬表"),
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc/horse.html", "出走馬情報"),
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc/data.html", "データ分析"),
                new SearchResultLink("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", "2026年5月8日（金曜） 第31回NHKマイルカップ（GⅠ）枠順確定"),
            ],
            Actions: [],
            Tables: []);

        var result = new JraGradeOneSpecialParser().Parse(snapshot);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual("NHKマイルカップ", result.Data.RaceName);
        Assert.AreEqual("GⅠ", result.Data.Grade);
        Assert.AreEqual("東京", result.Data.Racecourse);
        Assert.IsTrue(result.Data.Tabs.Any(tab => tab.Label == "出馬表"));
    }
}

// JRAサイト再設計（docs/jra-scraping.md）により、対象の JraStructuredPageParser は一時的に無効化されている。
#if false
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public class JraStructuredPageParserTests
{
    [TestMethod]
    public void Detect_KeibaMenuAndCalendarPageKinds()
    {
        var keibaMenu = new PageSnapshot(
            "https://www.jra.go.jp/keiba/",
            "競馬メニュー | JRA",
            [
            new PageSectionSnapshot(
                title: "競馬メニュー",
                mainText: "競馬メニュー 開催日程 出馬表 オッズ レース結果",
                headings: ["競馬メニュー"],
                links: [],
                actions: [],
                tables: [])
        ]);

        var calendar = new PageSnapshot(
            "https://www.jra.go.jp/keiba/calendar/may.html",
            "開催日程 2026年5月 | JRA",
            [
            new PageSectionSnapshot(
                title: "開催日程",
                mainText: "開催日程 2026年5月",
                headings: ["開催日程"],
                links: [],
                actions: [],
                tables: [])
            ,
            new PageSectionSnapshot(
                title: "5月",
                mainText: string.Empty,
                headings: ["5月"],
                links: [],
                actions: [],
                tables: [])
        ]);

        Assert.AreEqual(JraPageKind.KeibaMenu, JraPageKindDetector.Detect(keibaMenu.Url, keibaMenu));
        Assert.AreEqual(JraPageKind.ScheduleCalendar, JraPageKindDetector.Detect(calendar.Url, calendar));
    }

    [TestMethod]
    public void Detect_ThisWeekAndGradeOneSpecialPageKinds()
    {
        var thisWeek = new PageSnapshot(
            "https://www.jra.go.jp/keiba/thisweek/",
            "今週の注目レース | JRA",
            [
            new PageSectionSnapshot(
                title: "今週の注目レース",
                mainText: "今週の注目レース 5月9日～5月10日",
                headings: ["今週の注目レース"],
                links: [],
                actions: [],
                tables: [])
        ]);

        var gradeOne = new PageSnapshot(
            "https://www.jra.go.jp/keiba/g1/nmc.html",
            "NHKマイルカップ | JRA",
            [
            new PageSectionSnapshot(
                title: "NHKマイルカップ",
                mainText: "GⅠレース 2026年5月10日（日曜） 東京競馬場 1600メートル（芝） 出馬表",
                headings: ["NHKマイルカップ"],
                links: [],
                actions: [],
                tables: [])
        ]);

        Assert.AreEqual(JraPageKind.ThisWeekFeature, JraPageKindDetector.Detect(thisWeek.Url, thisWeek));
        Assert.AreEqual(JraPageKind.GradeOneSpecial, JraPageKindDetector.Detect(gradeOne.Url, gradeOne));
    }

    [TestMethod]
    public void KeibaMenuParser_ExtractsPrimaryEntriesAndScheduleLink()
    {
        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/keiba/",
            "競馬メニュー | JRA",
            [
            new PageSectionSnapshot(
                title: "競馬メニュー",
                mainText: "今週の開催情報",
                headings: ["競馬メニュー"],
                links:
                [
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/calendar/", "開催日程"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/thisweek/", "今週の注目レース"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/baba/index.html", "馬場情報"),
                ],
                actions:
                [
                    new PageActionSnapshot("出馬表", "link"),
                    new PageActionSnapshot("オッズ", "link"),
                ],
                tables: [])
            ,
            new PageSectionSnapshot(
                title: "今週の開催情報",
                mainText: string.Empty,
                headings: ["今週の開催情報"],
                links: [],
                actions: [],
                tables: [])
        ]);

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
            "https://www.jra.go.jp/keiba/calendar/may.html",
            "開催日程 2026年5月 | JRA",
            [
            new PageSectionSnapshot(
                title: "開催日程",
                mainText: "開催日程 2026年5月 9 地方 東京 エプソムC(GIII) 京都 京都新聞杯(GII) 新潟 10 地方 東京 NHKマイルC(GI) 京都 新潟",
                headings: ["開催日程"],
                links:
                [
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/calendar/apr.html", "4月"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/calendar/may.html", "5月"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/calendar/jun.html", "6月"),
                ],
                actions: [],
                tables:
                [
                    new PageTableSnapshot(
                        Headers: [],
                        Rows:
                        [
                            ["9 地方 東京 エプソムC(GIII) 京都 京都新聞杯(GII) 新潟"],
                            ["10 地方 東京 NHKマイルC(GI) 京都 新潟"],
                        ])
                ])
            ,
            new PageSectionSnapshot(
                title: "5月",
                mainText: string.Empty,
                headings: ["5月"],
                links: [],
                actions: [],
                tables: [])
        ]);

        var result = new JraScheduleCalendarParser().Parse(snapshot);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual(2026, result.Data.Year);
        Assert.AreEqual(5, result.Data.Month);
        Assert.HasCount(2, result.Data.ScheduledDays);
        CollectionAssert.AreEquivalent(new[] { "東京", "京都", "新潟" }, result.Data.ScheduledDays[0].Racecourses.ToList());
    }

    [TestMethod]
    public void ThisWeekFeatureParser_ExtractsFeaturedRaceLinks()
    {
        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/keiba/thisweek/",
            "今週の注目レース | JRA",
            [
            new PageSectionSnapshot(
                title: "今週の注目レース",
                mainText: "今週の注目レース 5月9日～5月10日",
                headings: ["今週の注目レース"],
                links:
                [
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc.html", "5月10日（日曜） NHKマイルカップ（GⅠ） 東京競馬場 芝1600メートル"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc.html", "レーストップ"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", "出馬表"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc/horse.html", "出走馬情報"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc/data.html", "データ分析"),
                ],
                actions: [],
                tables: [])
        ]);

        var result = new JraThisWeekFeatureParser().Parse(snapshot);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Data);
        Assert.HasCount(1, result.Data.FeaturedRaces);
        Assert.AreEqual("NHKマイルカップ", result.Data.FeaturedRaces[0].RaceName);
        Assert.AreEqual("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", result.Data.FeaturedRaces[0].RaceCardUrl);
    }

    [TestMethod]
    public void GradeOneSpecialParser_ExtractsMetadataAndTabs()
    {
        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/keiba/g1/nmc.html",
            "NHKマイルカップ | JRA",
            [
            new PageSectionSnapshot(
                title: "NHKマイルカップ",
                mainText: "GⅠレース 2026年5月10日（日曜） 東京競馬場 芝1600メートル NHKマイルカップ 関連ニュース 第31回NHKマイルカップ（GⅠ）枠順確定",
                headings: ["NHKマイルカップ"],
                links:
                [
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", "出馬表"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc/horse.html", "出走馬情報"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc/data.html", "データ分析"),
                    new PageLinkSnapshot("https://www.jra.go.jp/keiba/g1/nmc/syutsuba.html", "2026年5月8日（金曜） 第31回NHKマイルカップ（GⅠ）枠順確定"),
                ],
                actions: [],
                tables: [])
        ]);

        var result = new JraGradeOneSpecialParser().Parse(snapshot);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual("NHKマイルカップ", result.Data.RaceName);
        Assert.AreEqual("GⅠ", result.Data.Grade);
        Assert.AreEqual("東京", result.Data.Racecourse);
        Assert.IsTrue(result.Data.Tabs.Any(tab => tab.Label == "出馬表"));
    }

    [TestMethod]
    public void GradeOneSpecialParser_NormalizesRaceNameOnHorseInfoPage()
    {
        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/keiba/g1/nmc/horse.html",
            "出走馬情報 2026年NHKマイルカップ（GⅠ） JRA",
            [
            new PageSectionSnapshot(
                title: "NHKマイルカップ",
                mainText: "GⅠレース 2026年5月10日（日曜） 東京競馬場 芝1600メートル NHKマイルカップ 出馬表 出走馬情報 データ分析",
                headings: ["NHKマイルカップ"],
                links: [],
                actions: [],
                tables: [])
        ]);

        var result = new JraGradeOneSpecialParser().Parse(snapshot);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual("NHKマイルカップ", result.Data.RaceName);
    }
}
#endif

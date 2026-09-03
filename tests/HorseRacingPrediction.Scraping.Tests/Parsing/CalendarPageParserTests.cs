using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class CalendarPageParserTests
{
    private const string Url = "https://www.jra.go.jp/keiba/calendar/";

    private static PageSnapshot BuildSnapshot()
    {
        var table = new PageTableSnapshot(
            Headers: ["日", "月", "火", "水", "木", "金", "土"],
            Rows:
            [
                ["1", "2", "3", "4", "5 地方 中山 京成杯オータムH(GⅢ) 阪神 札幌 札幌2歳S(GⅢ)", "6", "7"],
                ["8", "9", "10", "11", "12", "13 中山", "14"],
            ]);

        var section = new PageSectionSnapshot(
            title: "開催日程",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["開催日程>2026年9月"]);

        return new PageSnapshot(Url, "開催日程", [section]);
    }

    [TestMethod]
    public void CanParse_CalendarUrl_ReturnsTrue()
    {
        var parser = new CalendarPageParser();

        Assert.IsTrue(parser.CanParse(BuildSnapshot()));
    }

    [TestMethod]
    public void Parse_ReturnsExpectedMonthAndRaceDates()
    {
        var parser = new CalendarPageParser();

        var page = (JraCalendarPage)parser.Parse(BuildSnapshot());

        Assert.AreEqual(new YearMonth(2026, 9), page.Month);
        Assert.AreEqual(2, page.RaceDates.Count);

        var day5 = page.RaceDates.Single(x => x.Date == new DateOnly(2026, 9, 5));
        CollectionAssert.AreEqual(
            new[] { RaceCourse.Nakayama, RaceCourse.Hanshin, RaceCourse.Sapporo },
            day5.Courses.ToArray());

        var day13 = page.RaceDates.Single(x => x.Date == new DateOnly(2026, 9, 13));
        CollectionAssert.AreEqual(
            new[] { RaceCourse.Nakayama },
            day13.Courses.ToArray());
    }

    [TestMethod]
    public void Parse_MonthNotFound_Throws()
    {
        var section = new PageSectionSnapshot(
            title: "開催日程",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [],
            headings: []);

        var snapshot = new PageSnapshot(Url, "開催日程", [section]);

        var parser = new CalendarPageParser();

        Assert.ThrowsExactly<JraPageParseException>(
            () => parser.Parse(snapshot));
    }
}

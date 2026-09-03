using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

[TestClass]
public sealed class JraScheduleCollectionWorkflowTests
{
    private static JraSession CreateSession(IJraPage calendarResult)
    {
        var browser = new FakeWebBrowser();
        var navigator = new FakeJraNavigator(calendarResult);
        var pageReader = new JraPageReader(browser, []);

        return new JraSession(browser, navigator, pageReader);
    }

    [TestMethod]
    public async Task CollectAsync_開催日あり_競馬場一覧を返す()
    {
        var date = new DateOnly(2026, 9, 6);
        var calendar = new JraCalendarPage(
            "https://example.jra.go.jp/calendar",
            new YearMonth(2026, 9),
            [
                new JraRaceDate(date, [RaceCourse.Hanshin, RaceCourse.Chukyo]),
            ]);

        await using var session = CreateSession(calendar);
        var workflow = new JraScheduleCollectionWorkflow(session);

        var result = await workflow.CollectAsync(date);

        CollectionAssert.AreEqual(
            new[] { RaceCourse.Hanshin, RaceCourse.Chukyo },
            result.ToArray());
    }

    [TestMethod]
    public async Task CollectAsync_開催なし_空配列を返す()
    {
        var date = new DateOnly(2026, 9, 7);
        var calendar = new JraCalendarPage(
            "https://example.jra.go.jp/calendar",
            new YearMonth(2026, 9),
            []);

        await using var session = CreateSession(calendar);
        var workflow = new JraScheduleCollectionWorkflow(session);

        var result = await workflow.CollectAsync(date);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task CollectAsync_想定外ページ_JraCollectionExceptionを投げる()
    {
        var date = new DateOnly(2026, 9, 6);
        var unexpected = new JraUnknownPage(
            "https://example.jra.go.jp/unexpected",
            "想定外ページ");

        await using var session = CreateSession(unexpected);
        var workflow = new JraScheduleCollectionWorkflow(session);

        await Assert.ThrowsExactlyAsync<JraCollectionException>(
            () => workflow.CollectAsync(date));
    }
}

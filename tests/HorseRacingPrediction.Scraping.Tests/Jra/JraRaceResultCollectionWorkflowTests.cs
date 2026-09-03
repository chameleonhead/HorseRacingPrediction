using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

[TestClass]
public sealed class JraRaceResultCollectionWorkflowTests
{
    private static readonly DateOnly Date = new(2026, 9, 6);
    private const RaceCourse Course = RaceCourse.Tokyo;
    private static readonly RaceId TestRaceId = new(Date, Course, 5);

    private static (JraSession Session, FakeJraNavigator Navigator, FakeDataCollectionWriteService WriteService) CreateContext(
        IReadOnlyDictionary<RaceId, IJraPage> resultsByRaceId)
    {
        var browser = new FakeWebBrowser();
        var navigator = new FakeJraNavigator(resultsByRaceId);
        var pageReader = new JraPageReader(browser, []);
        var session = new JraSession(browser, navigator, pageReader);
        var writeService = new FakeDataCollectionWriteService();

        return (session, navigator, writeService);
    }

    private static JraRaceResultPage CreateResultPage(RaceId raceId, params RaceResultEntry[] entries)
        => new(
            $"https://example.jra.go.jp/result/{raceId.Number}",
            raceId,
            "テストレース",
            entries);

    [TestMethod]
    public async Task CollectAsync_1着2着3着_それぞれDeclareRaceEntryResultAsyncが呼ばれる()
    {
        var entries = new[]
        {
            new RaceResultEntry(1, 3, "テストホースA", "テスト騎手A", TimeSpan.FromSeconds(84.5)),
            new RaceResultEntry(2, 7, "テストホースB", "テスト騎手B", TimeSpan.FromSeconds(85.0)),
            new RaceResultEntry(3, 1, "テストホースC", "テスト騎手C", TimeSpan.FromSeconds(85.3)),
        };
        var resultPage = CreateResultPage(TestRaceId, entries);

        var (session, navigator, writeService) = CreateContext(
            new Dictionary<RaceId, IJraPage> { [TestRaceId] = resultPage });

        await using var _ = session;
        var workflow = new JraRaceResultCollectionWorkflow(session, writeService);

        var result = await workflow.CollectAsync(TestRaceId);

        var expectedRaceId = DeterministicIdGenerator.BuildRaceId(Date, "東京", TestRaceId.Number);
        Assert.AreEqual(TestRaceId, result.RaceId);
        Assert.AreEqual(expectedRaceId, result.DataCollectionRaceId);
        Assert.IsEmpty(result.Errors);
        CollectionAssert.AreEqual(new[] { 3, 7, 1 }, result.SavedHorseNumbers.ToArray());

        Assert.HasCount(3, writeService.DeclareRaceEntryResultCalls);

        var call1 = writeService.DeclareRaceEntryResultCalls[0];
        Assert.AreEqual(expectedRaceId, call1.RaceId);
        Assert.AreEqual(3, call1.HorseNumber);
        Assert.AreEqual(1, call1.FinishPosition);
        Assert.AreEqual("1:24.5", call1.OfficialTime);

        var call2 = writeService.DeclareRaceEntryResultCalls[1];
        Assert.AreEqual(expectedRaceId, call2.RaceId);
        Assert.AreEqual(7, call2.HorseNumber);
        Assert.AreEqual(2, call2.FinishPosition);
        Assert.AreEqual("1:25.0", call2.OfficialTime);

        var call3 = writeService.DeclareRaceEntryResultCalls[2];
        Assert.AreEqual(expectedRaceId, call3.RaceId);
        Assert.AreEqual(1, call3.HorseNumber);
        Assert.AreEqual(3, call3.FinishPosition);
        Assert.AreEqual("1:25.3", call3.OfficialTime);

        CollectionAssert.AreEqual(new[] { TestRaceId }, navigator.RequestedRaceResults.ToArray());
    }

    [TestMethod]
    public async Task CollectAsync_1エントリー失敗しても他のエントリーは保存される()
    {
        var entries = new[]
        {
            new RaceResultEntry(1, 3, "テストホースA", "テスト騎手A", TimeSpan.FromSeconds(84.5)),
            new RaceResultEntry(2, 7, "テストホースB", "テスト騎手B", TimeSpan.FromSeconds(85.0)),
        };
        var resultPage = CreateResultPage(TestRaceId, entries);

        var (session, _, writeService) = CreateContext(
            new Dictionary<RaceId, IJraPage> { [TestRaceId] = resultPage });
        writeService.FailForHorseNumber = 3;

        await using var _ = session;
        var workflow = new JraRaceResultCollectionWorkflow(session, writeService);

        var result = await workflow.CollectAsync(TestRaceId);

        Assert.HasCount(1, result.SavedHorseNumbers);
        Assert.AreEqual(7, result.SavedHorseNumbers[0]);
        Assert.HasCount(1, result.Errors);
        Assert.Contains("HorseNumber=3", result.Errors[0]);
    }

    [TestMethod]
    public async Task CollectAsync_RaceCourseUnknown_ArgumentExceptionを投げる()
    {
        var (session, _, writeService) = CreateContext(new Dictionary<RaceId, IJraPage>());
        await using var _ = session;
        var workflow = new JraRaceResultCollectionWorkflow(session, writeService);

        var unknownRaceId = new RaceId(Date, RaceCourse.Unknown, 1);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => workflow.CollectAsync(unknownRaceId));
    }

    [TestMethod]
    public async Task CollectAsync_想定外ページ_JraCollectionExceptionを投げる()
    {
        var browser = new FakeWebBrowser();
        var unexpected = new JraUnknownPage("https://example.jra.go.jp/unexpected", "想定外ページ");
        var navigator = new FakeJraNavigator(
            new Dictionary<RaceId, IJraPage> { [TestRaceId] = unexpected });
        var pageReader = new JraPageReader(browser, []);
        var session = new JraSession(browser, navigator, pageReader);
        var writeService = new FakeDataCollectionWriteService();

        await using var _ = session;
        var workflow = new JraRaceResultCollectionWorkflow(session, writeService);

        await Assert.ThrowsExactlyAsync<JraCollectionException>(
            () => workflow.CollectAsync(TestRaceId));
    }
}

using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

[TestClass]
public sealed class JraRaceCardCollectionWorkflowTests
{
    private static readonly DateOnly Date = new(2026, 9, 6);
    private const RaceCourse Course = RaceCourse.Tokyo;

    private static (JraSession Session, FakeJraNavigator Navigator, FakeDataCollectionWriteService WriteService) CreateContext(
        JraRaceListPage raceList,
        IReadOnlyDictionary<RaceId, IJraPage> cardsByRaceId)
    {
        var browser = new FakeWebBrowser();
        var navigator = new FakeJraNavigator(raceList, cardsByRaceId);
        var pageReader = new JraPageReader(browser, []);
        var session = new JraSession(browser, navigator, pageReader);
        var writeService = new FakeDataCollectionWriteService();

        return (session, navigator, writeService);
    }

    private static JraRaceListPage CreateRaceList(params RaceSummary[] races)
        => new(
            "https://example.jra.go.jp/race-list",
            Date,
            Course,
            races);

    private static RaceSummary CreateRaceSummary(int number)
        => new(
            new RaceId(Date, Course, number),
            $"{number}R テストレース",
            new TimeOnly(10, 0),
            $"https://example.jra.go.jp/card/{number}",
            $"https://example.jra.go.jp/result/{number}");

    private static JraRaceCardPage CreateRaceCard(RaceId raceId, string raceName, params RaceEntry[] entries)
        => new(
            $"https://example.jra.go.jp/card/{raceId.Number}",
            raceId,
            raceName,
            new TimeOnly(10, 0),
            entries);

    [TestMethod]
    public async Task CollectAsync_2レース2頭ずつ_全レース保存される()
    {
        var race1 = CreateRaceSummary(1);
        var race2 = CreateRaceSummary(2);
        var raceList = CreateRaceList(race1, race2);

        var card1 = CreateRaceCard(
            race1.Id,
            "1R テストレース",
            new RaceEntry(1, "テストホースA", 1, "テスト騎手A", 55.0m),
            new RaceEntry(2, "テストホースB", 2, "テスト騎手B", 54.0m));

        var card2 = CreateRaceCard(
            race2.Id,
            "2R テストレース",
            new RaceEntry(1, "テストホースC", 1, "テスト騎手C", 56.0m),
            new RaceEntry(2, "テストホースD", 2, "テスト騎手D", 53.0m));

        var (session, navigator, writeService) = CreateContext(
            raceList,
            new Dictionary<RaceId, IJraPage>
            {
                [race1.Id] = card1,
                [race2.Id] = card2,
            });

        await using var _ = session;
        var workflow = new JraRaceCardCollectionWorkflow(session, writeService);

        var result = await workflow.CollectAsync(Date, Course);

        Assert.AreEqual(Date, result.Date);
        Assert.AreEqual(Course, result.Course);
        Assert.IsEmpty(result.Errors);
        Assert.HasCount(2, result.RaceIds);

        Assert.HasCount(2, writeService.UpsertRaceCalls);
        Assert.AreEqual("2026-09-06", writeService.UpsertRaceCalls[0].RaceDate);
        Assert.AreEqual("東京", writeService.UpsertRaceCalls[0].RacecourseCode);
        Assert.AreEqual(1, writeService.UpsertRaceCalls[0].RaceNumber);
        Assert.AreEqual("1R テストレース", writeService.UpsertRaceCalls[0].RaceName);
        Assert.AreEqual(2, writeService.UpsertRaceCalls[0].EntryCount);

        Assert.AreEqual(2, writeService.UpsertRaceCalls[1].RaceNumber);

        var expectedRaceId1 = $"race-2026-09-06-東京-1";
        var expectedRaceId2 = $"race-2026-09-06-東京-2";
        CollectionAssert.AreEqual(new[] { expectedRaceId1, expectedRaceId2 }, result.RaceIds.ToArray());

        Assert.HasCount(4, writeService.UpsertRaceEntryCalls);

        var entry1 = writeService.UpsertRaceEntryCalls[0];
        Assert.AreEqual(expectedRaceId1, entry1.RaceId);
        Assert.AreEqual(1, entry1.HorseNumber);
        Assert.AreEqual("テストホースA", entry1.HorseName);
        Assert.AreEqual("テスト騎手A", entry1.JockeyName);
        Assert.AreEqual(1, entry1.GateNumber);
        Assert.AreEqual(55.0m, entry1.AssignedWeight);

        var entry3 = writeService.UpsertRaceEntryCalls[2];
        Assert.AreEqual(expectedRaceId2, entry3.RaceId);
        Assert.AreEqual(1, entry3.HorseNumber);
        Assert.AreEqual("テストホースC", entry3.HorseName);

        CollectionAssert.AreEqual(new[] { race1.Id, race2.Id }, navigator.RequestedRaceCards.ToArray());
    }

    [TestMethod]
    public async Task CollectAsync_出馬表に含まれる馬主名がそのまま登録される()
    {
        var race1 = CreateRaceSummary(1);
        var raceList = CreateRaceList(race1);

        var card1 = CreateRaceCard(
            race1.Id,
            "1R テストレース",
            new RaceEntry(1, "テストホースA", 1, "テスト騎手A", 55.0m, "テスト調教師A", "テスト馬主A"));

        var (session, _, writeService) = CreateContext(
            raceList,
            new Dictionary<RaceId, IJraPage> { [race1.Id] = card1 });

        await using var _ = session;
        var workflow = new JraRaceCardCollectionWorkflow(session, writeService);

        var result = await workflow.CollectAsync(Date, Course);

        Assert.IsEmpty(result.Errors);
        Assert.HasCount(1, writeService.UpsertHorseWithOwnerCalls);
        Assert.AreEqual("テストホースA", writeService.UpsertHorseWithOwnerCalls[0].RegisteredName);
        Assert.AreEqual("テスト馬主A", writeService.UpsertHorseWithOwnerCalls[0].OwnerName);
    }

    [TestMethod]
    public async Task CollectAsync_馬主名が無くても出走登録は継続する()
    {
        var race1 = CreateRaceSummary(1);
        var raceList = CreateRaceList(race1);

        var card1 = CreateRaceCard(
            race1.Id,
            "1R テストレース",
            new RaceEntry(1, "テストホースA", 1, "テスト騎手A", 55.0m));

        var (session, _, writeService) = CreateContext(
            raceList,
            new Dictionary<RaceId, IJraPage> { [race1.Id] = card1 });

        await using var _ = session;
        var workflow = new JraRaceCardCollectionWorkflow(session, writeService);

        var result = await workflow.CollectAsync(Date, Course);

        Assert.IsEmpty(result.Errors);
        Assert.HasCount(1, result.RaceIds);
        Assert.HasCount(1, writeService.UpsertRaceEntryCalls);
        Assert.IsEmpty(writeService.UpsertHorseWithOwnerCalls);
    }

    [TestMethod]
    public async Task CollectAsync_1レース失敗しても他のレースは保存される()
    {
        var race1 = CreateRaceSummary(1);
        var race2 = CreateRaceSummary(2);
        var raceList = CreateRaceList(race1, race2);

        var card2 = CreateRaceCard(
            race2.Id,
            "2R テストレース",
            new RaceEntry(1, "テストホースC", 1, "テスト騎手C", 56.0m));

        // race1 の出馬表は未設定 -> ToRaceCardAsync が NotSupportedException を投げる
        var (session, _, writeService) = CreateContext(
            raceList,
            new Dictionary<RaceId, IJraPage>
            {
                [race2.Id] = card2,
            });

        await using var _ = session;
        var workflow = new JraRaceCardCollectionWorkflow(session, writeService);

        var result = await workflow.CollectAsync(Date, Course);

        Assert.HasCount(1, result.RaceIds);
        Assert.HasCount(1, result.Errors);
        Assert.Contains("RaceNumber=1", result.Errors[0]);
        Assert.HasCount(1, writeService.UpsertRaceCalls);
        Assert.AreEqual(2, writeService.UpsertRaceCalls[0].RaceNumber);
    }

    [TestMethod]
    public async Task CollectAsync_RaceCourseUnknown_ArgumentExceptionを投げる()
    {
        var raceList = CreateRaceList();
        var (session, _, writeService) = CreateContext(raceList, new Dictionary<RaceId, IJraPage>());

        await using var _ = session;
        var workflow = new JraRaceCardCollectionWorkflow(session, writeService);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => workflow.CollectAsync(Date, RaceCourse.Unknown));
    }

    [TestMethod]
    public async Task CollectAsync_出馬表未公開_エラーなしの空結果を返す()
    {
        var browser = new FakeWebBrowser();
        var navigator = new FakeJraNavigator(new Dictionary<RaceId, IJraPage>());
        navigator.SetRaceListException(
            new JraNavigationException(
                "未公開です。",
                JraNavigationFailureReason.NotYetPublished));
        var pageReader = new JraPageReader(browser, []);
        var session = new JraSession(browser, navigator, pageReader);
        var writeService = new FakeDataCollectionWriteService();

        await using var _ = session;
        var workflow = new JraRaceCardCollectionWorkflow(session, writeService);

        var result = await workflow.CollectAsync(Date, Course);

        // 出馬表未公開は業務的に正常な状態であり、呼び出し元（Collectorジョブ）が
        // 「再試行すべきか」を判断できるよう、エラーとしてではなく空の結果として返す。
        Assert.IsEmpty(result.RaceIds);
        Assert.IsEmpty(result.Errors);
        Assert.IsEmpty(result.Races);
    }

    [TestMethod]
    public async Task CollectAsync_レース一覧取得が範囲外エラー_この競馬場分のみエラーとして記録される()
    {
        var browser = new FakeWebBrowser();
        var navigator = new FakeJraNavigator(new Dictionary<RaceId, IJraPage>());
        navigator.SetRaceListException(
            new JraNavigationException(
                "範囲外です。",
                JraNavigationFailureReason.OutOfDisplayedRange));
        var pageReader = new JraPageReader(browser, []);
        var session = new JraSession(browser, navigator, pageReader);
        var writeService = new FakeDataCollectionWriteService();

        await using var _ = session;
        var workflow = new JraRaceCardCollectionWorkflow(session, writeService);

        var result = await workflow.CollectAsync(Date, Course);

        Assert.IsEmpty(result.RaceIds);
        Assert.HasCount(1, result.Errors);
        Assert.Contains("範囲外です。", result.Errors[0]);
    }

    [TestMethod]
    public async Task CollectAsync_想定外ページ_JraCollectionExceptionを投げる()
    {
        var browser = new FakeWebBrowser();
        var unexpected = new JraUnknownPage("https://example.jra.go.jp/unexpected", "想定外ページ");
        var navigator = new FakeJraNavigator(unexpected, new Dictionary<RaceId, IJraPage>());
        var pageReader = new JraPageReader(browser, []);
        var session = new JraSession(browser, navigator, pageReader);
        var writeService = new FakeDataCollectionWriteService();

        await using var _ = session;
        var workflow = new JraRaceCardCollectionWorkflow(session, writeService);

        await Assert.ThrowsExactlyAsync<JraCollectionException>(
            () => workflow.CollectAsync(Date, Course));
    }
}

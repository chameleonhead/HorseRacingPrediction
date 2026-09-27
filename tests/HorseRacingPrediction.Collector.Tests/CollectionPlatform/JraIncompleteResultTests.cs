using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Navigation;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraIncompleteResultTests
{
    private static readonly DateOnly RaceDate = new(2026, 9, 27);
    private static readonly RaceId Race = new(RaceDate, RaceCourse.Nakayama, 1);
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 1, 10, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OfficialStart = new(2026, 9, 27, 1, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task DirectResult_IncompleteTime_WaitsFiveMinutesWithoutPersistingResult()
    {
        var cardUrl = new Uri("https://example.test/card/1");
        var resultUrl = new Uri("https://example.test/result/1");
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = _ => throw Incomplete(Race, resultUrl),
        };
        var sessions = SessionReturningCardThenIncomplete(cardUrl, resultUrl);
        var handler = CreateHandler(sessions, results);

        var completion = await handler.CollectAsync(CreateTask(
            [new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null, RaceArtifactKind.Card),
             new(2, resultUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null, RaceArtifactKind.Result)]),
            CancellationToken.None);

        AssertWait(completion);
        Assert.AreEqual(Now.AddMinutes(5), completion.RetryAt);
        Assert.IsEmpty(results.Requests);
        Assert.IsTrue(completion.StageOutcomes!.Any(x => x.ErrorCode == "RecentResultIncomplete" && !x.Persisted));
        Assert.AreEqual(resultUrl, completion.FinalUrl);
    }

    [TestMethod]
    public async Task FallbackResult_IncompleteTime_WaitsWithoutPersistingResult()
    {
        var cardUrl = new Uri("https://example.test/card/1");
        var resultUrl = new Uri("https://example.test/result/1");
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ThrowOnCollect = Incomplete(Race, resultUrl),
        };
        var sessions = SessionReturningCard(cardUrl, Race);
        var handler = CreateHandler(sessions, results);

        var completion = await handler.CollectAsync(CreateTask(
            [new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null, RaceArtifactKind.Card)]),
            CancellationToken.None);

        AssertWait(completion);
        Assert.AreEqual(Now.AddMinutes(5), completion.RetryAt);
        Assert.HasCount(1, results.Requests);
        Assert.IsTrue(completion.StageOutcomes!.Any(x => x.ErrorCode == "RecentResultIncomplete" && !x.Persisted));
    }

    [TestMethod]
    [DataRow("no-start")]
    [DataRow("before-start")]
    [DataRow("expired")]
    [DataRow("different-race")]
    public async Task IncompleteTime_OutsideTemporaryWindow_RemainsFailure(string caseName)
    {
        var cardUrl = new Uri("https://example.test/card/1");
        var resultUrl = new Uri("https://example.test/result/1");
        var exceptionRace = caseName == "different-race"
            ? new RaceId(RaceDate, RaceCourse.Nakayama, 2)
            : Race;
        var exception = Incomplete(exceptionRace, resultUrl);
        var results = new FakeJraRaceResultCollectionWorkflow { ThrowOnCollect = exception };
        var sessions = SessionReturningCard(cardUrl, Race, null);
        var handler = CreateHandler(sessions, results);
        var attributes = new Dictionary<string, string>
        {
            ["course"] = "中山",
            ["number"] = "1",
        };
        if (caseName != "no-start")
        {
            var start = caseName == "before-start" ? Now.AddMinutes(10)
                : caseName == "expired" ? Now.AddMinutes(-31) : OfficialStart;
            attributes["officialStartAt"] = start.ToString("O");
        }

        try
        {
            var completion = await handler.CollectAsync(CreateTask(
                [new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null, RaceArtifactKind.Card)],
                attributes), CancellationToken.None);
            Assert.AreNotEqual("RecentResultIncomplete", completion.ErrorCode, caseName);
        }
        catch (JraIncompleteResultException) when (caseName is "expired" or "different-race")
        {
            // Outside the approved gate, the typed parse error must remain a failure.
        }
    }

    [TestMethod]
    public async Task IncompleteTime_DeadlineCapsRetryAtThirtyMinutes()
    {
        var cardUrl = new Uri("https://example.test/card/1");
        var resultUrl = new Uri("https://example.test/result/1");
        var results = new FakeJraRaceResultCollectionWorkflow { ThrowOnCollect = Incomplete(Race, resultUrl) };
        var sessions = SessionReturningCard(cardUrl, Race, new(10, 0));
        var handler = CreateHandler(sessions, results, new FixedTimeProvider(
            OfficialStart.AddMinutes(28)));

        var completion = await handler.CollectAsync(CreateTask(
            [new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null, RaceArtifactKind.Card)],
            new Dictionary<string, string>
            {
                ["course"] = "中山",
                ["number"] = "1",
                ["officialStartAt"] = OfficialStart.ToString("O")
            }), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, completion.Result);
        Assert.AreEqual(OfficialStart.AddMinutes(30), completion.RetryAt);
    }

    private static FakeJraSessionFactory SessionReturningCard(Uri cardUrl, RaceId race, TimeOnly? start = null)
        => new()
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => url == cardUrl
                    ? new JraRaceCardPage(url.AbsoluteUri, race, "検証レース", start, [])
                    : throw new JraNavigationException("unexpected URL"),
            },
        };

    [TestMethod]
    public async Task PreviousJstDay_DoesNotReceiveTemporaryWindowEvenWithRecentTimestamp()
    {
        var cardUrl = new Uri("https://example.test/card/1");
        var nextDay = new DateTimeOffset(2026, 9, 27, 15, 5, 0, TimeSpan.Zero);
        var results = new FakeJraRaceResultCollectionWorkflow
        { ThrowOnCollect = Incomplete(Race, new("https://example.test/result/1")) };
        var handler = CreateHandler(SessionReturningCard(cardUrl, Race), results, new FixedTimeProvider(nextDay));
        var task = CreateTask([new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null, RaceArtifactKind.Card)],
            new Dictionary<string, string> { ["course"] = "中山", ["number"] = "1", ["officialStartAt"] = nextDay.AddMinutes(-10).ToString("O") });
        await Assert.ThrowsExactlyAsync<JraIncompleteResultException>(() => handler.CollectAsync(task, CancellationToken.None));
    }

    private static FakeJraSessionFactory SessionReturningCardThenIncomplete(Uri cardUrl, Uri resultUrl)
        => new()
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => url == cardUrl
                    ? new JraRaceCardPage(url.AbsoluteUri, Race, "検証レース", new(10, 0), [])
                    : throw Incomplete(Race, resultUrl),
            },
        };

    private static JraRaceDetailCollectionHandler CreateHandler(
        FakeJraSessionFactory sessions, FakeJraRaceResultCollectionWorkflow results,
        TimeProvider? time = null)
        => new(sessions, _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: time ?? new FixedTimeProvider(Now));

    private static JraIncompleteResultException Incomplete(RaceId race, Uri url)
        => new(url.AbsoluteUri, race, 3, ["着順", "馬番", "タイム"], ["1", "3", ""]);

    private static LeasedCollectionTask CreateTask(
        IReadOnlyList<ResourceLocationCandidate> locations,
        IReadOnlyDictionary<string, string>? attributes = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), new(ResourceType.Race, "JRA", Race.ToString()),
            new("race-detail"), 1, CollectionReason.ManualRefresh, CollectionLane.Realtime, 100,
            "lease", Now.AddMinutes(5), RaceDate,
            attributes ?? new Dictionary<string, string>
            {
                ["course"] = "中山",
                ["number"] = "1",
                ["officialStartAt"] = OfficialStart.ToString("O"),
            }, locations);

    private static void AssertWait(CollectionAttemptCompletion completion)
    {
        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, completion.Result);
        Assert.AreEqual("RecentResultIncomplete", completion.ErrorCode);
        Assert.IsNotNull(completion.RetryAt);
        Assert.IsFalse(completion.StageOutcomes!.Any(x => x.Stage == "PersistResult" && x.Persisted));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraDirectCollectionHandlerTests
{
    [TestMethod]
    public async Task RaceCard_UsesExplicitCandidateAndValidatesRaceIdentity()
    {
        var date = new DateOnly(2026, 9, 12);
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var direct = new Uri("https://example.test/card/11");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => new JraRaceCardPage(direct.AbsoluteUri, race, "test", new(15, 30), []),
            },
        };
        var workflow = new FakeJraRaceCardCollectionWorkflow();
        var handler = new JraRaceCardCollectionHandler(sessions, _ => workflow);
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.RaceCard, "JRA", "20260912:Tokyo:11"), new("race-card"), 1,
            CollectionReason.ManualRefresh, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11" },
            [new(0, direct, ResourceLocationSource.Explicit, ResourceLocationStatus.Unknown, null)]);

        var result = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.AreEqual(direct, result.RequestedUrl);
        Assert.HasCount(1, sessions.LastNavigator!.DirectUrlRequests);
        Assert.HasCount(1, workflow.RefreshRequests);
        Assert.IsNull(workflow.RefreshRequests[0].Target,
            "A discovered resource without a domainRaceId must use create/upsert semantics.");
    }

    [TestMethod]
    public async Task RaceCard_TriesNextCandidateAfterUnexpectedRace()
    {
        var date = new DateOnly(2026, 9, 12);
        var expected = new RaceId(date, RaceCourse.Tokyo, 11);
        var wrong = new Uri("https://example.test/card/wrong");
        var valid = new Uri("https://example.test/card/11");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => new JraRaceCardPage(url.AbsoluteUri,
                    url == valid ? expected : expected with { Number = 10 }, "test", new(15, 30), []),
            },
        };
        var workflow = new FakeJraRaceCardCollectionWorkflow();
        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow)
            .CollectAsync(CreateTask(ResourceType.RaceCard, "race-card", date, wrong, valid), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.AreEqual(valid, result.RequestedUrl);
        Assert.IsNotNull(result.LocationOutcomes);
        Assert.AreEqual(CollectionAttemptResult.UnexpectedPage, result.LocationOutcomes[0].Result);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.LocationOutcomes[1].Result);
        CollectionAssert.AreEqual(new[] { wrong, valid }, sessions.LastNavigator!.DirectUrlRequests);
        Assert.HasCount(1, workflow.RefreshRequests);
    }

    [TestMethod]
    public async Task RaceCard_TriesNextCandidateAfterTransientNavigationFailure()
    {
        var date = new DateOnly(2026, 9, 12);
        var first = new Uri("https://example.test/card/timeout");
        var second = new Uri("https://example.test/card/11");
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => url == first
                    ? throw new TimeoutException("temporary")
                    : new JraRaceCardPage(url.AbsoluteUri, race, "test", new(15, 30), []),
            },
        };
        var workflow = new FakeJraRaceCardCollectionWorkflow();
        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow)
            .CollectAsync(CreateTask(ResourceType.RaceCard, "race-card", date, first, second), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.AreEqual(second, result.RequestedUrl);
        Assert.IsNotNull(result.LocationOutcomes);
        Assert.AreEqual(CollectionAttemptResult.TransientFailure, result.LocationOutcomes[0].Result);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.LocationOutcomes[1].Result);
        CollectionAssert.AreEqual(new[] { first, second }, sessions.LastNavigator!.DirectUrlRequests);
    }

    [TestMethod]
    public async Task RaceCard_AllCandidatesInvalid_FallsBackToDiscoveryWorkflow()
    {
        var date = new DateOnly(2026, 9, 12);
        var invalid = new Uri("https://example.test/not-a-card");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => new JraUnknownPage(url.AbsoluteUri, "unexpected"),
            },
        };
        var workflow = new FakeJraRaceCardCollectionWorkflow();
        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow)
            .CollectAsync(CreateTask(ResourceType.RaceCard, "race-card", date, invalid), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.AreEqual(new Uri("https://example.test/card"), result.RequestedUrl);
        Assert.IsNotNull(result.LocationOutcomes);
        Assert.AreEqual(CollectionAttemptResult.UnexpectedPage, result.LocationOutcomes.Single().Result);
        Assert.HasCount(1, workflow.RefreshRequests);
    }

    [TestMethod]
    public async Task RaceCard_CurrentDateNotPublished_IsRetryableAvailabilityState()
    {
        var date = DateOnly.FromDateTime(DateTime.Today);
        var sessions = new FakeJraSessionFactory();
        var workflow = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraCollectionException("出馬表を取得できませんでした。")
        };
        var task = CreateTask(ResourceType.RaceCard, "race-card", date);

        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow)
            .CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, result.Result);
        Assert.AreEqual("RaceCardNotYetAvailable", result.ErrorCode);
        Assert.IsNotNull(result.RetryAt);
    }

    [TestMethod]
    public async Task RaceResult_ValidatesTypeAndRaceBeforeUsingCandidate()
    {
        var date = new DateOnly(2026, 9, 12);
        var wrongType = new Uri("https://example.test/card/11");
        var wrongRace = new Uri("https://example.test/result/10");
        var valid = new Uri("https://example.test/result/11");
        var expected = new RaceId(date, RaceCourse.Tokyo, 11);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => url == wrongType
                    ? new JraRaceCardPage(url.AbsoluteUri, expected, "test", new(15, 30), [])
                    : new JraRaceResultPage(url.AbsoluteUri,
                        url == valid ? expected : expected with { Number = 10 }, "test", []),
            },
        };
        var workflow = new FakeJraRaceResultCollectionWorkflow();
        var result = await new JraRaceResultCollectionHandler(sessions, _ => workflow)
            .CollectAsync(CreateTask(ResourceType.RaceResult, "race-result", date, wrongType, wrongRace, valid),
                CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.AreEqual(valid, result.RequestedUrl);
        CollectionAssert.AreEqual(new[] { wrongType, wrongRace, valid }, sessions.LastNavigator!.DirectUrlRequests);
        Assert.HasCount(1, workflow.RefreshPageRequests);
        Assert.IsNull(workflow.RefreshPageRequests[0].Target,
            "A discovered result without a domainRaceId must create the race instead of refreshing its resource key.");
        Assert.IsEmpty(workflow.Requests);
    }

    private static LeasedCollectionTask CreateTask(ResourceType type, string definition, DateOnly date,
        params Uri[] locations) => new(Guid.NewGuid(), Guid.NewGuid(),
        new(type, "JRA", "20260912:Tokyo:11"), new(definition), 1,
        CollectionReason.ManualRefresh, CollectionLane.Realtime, 100, "lease",
        DateTimeOffset.UtcNow.AddMinutes(5), date,
        new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11" },
        locations.Select((url, index) => new ResourceLocationCandidate(index, url,
            ResourceLocationSource.Discovered, ResourceLocationStatus.Unknown, null)).ToArray());
}

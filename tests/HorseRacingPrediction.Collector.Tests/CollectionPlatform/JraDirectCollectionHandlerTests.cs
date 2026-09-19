using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraDirectCollectionHandlerTests
{
    [TestMethod]
    public void RaceDetail_RejectedSubjectBatchPreservesItemAndErrorCode()
    {
        var item = new CollectionRequestBulkItem("Horse:horse-1", "Horse", "JRA", "horse-1",
            "horse-profile", 3, "Discovery", "Normal", 50, null, new DateOnly(2026, 9, 19),
            new Dictionary<string, string>());
        var response = new CollectionRequestBulkResponse(
            [new(item.ItemKey, "Rejected", ErrorCode: "ResourceSuppressed")]);

        var exception = Assert.ThrowsExactly<JraRaceDetailCollectionHandler.ReferencedSubjectBatchException>(() =>
            JraRaceDetailCollectionHandler.ValidateReferencedSubjectBatchResponse([item], response));

        StringAssert.Contains(exception.Message, "Horse:horse-1:ResourceSuppressed");
    }

    [TestMethod]
    public void RaceDetail_MissingSubjectBatchOutcomePreservesMissingItem()
    {
        var item = new CollectionRequestBulkItem("Trainer:trainer-1", "Trainer", "JRA", "trainer-1",
            "trainer-profile", 3, "Discovery", "Normal", 50, null, new DateOnly(2026, 9, 19),
            new Dictionary<string, string>());

        var exception = Assert.ThrowsExactly<JraRaceDetailCollectionHandler.ReferencedSubjectBatchException>(() =>
            JraRaceDetailCollectionHandler.ValidateReferencedSubjectBatchResponse([item],
                new CollectionRequestBulkResponse([])));

        StringAssert.Contains(exception.Message, "Trainer:trainer-1:Missing");
    }

    [TestMethod]
    public void RaceDetail_LegacyTaskWithoutAttributes_ParsesCanonicalResourceId()
    {
        var date = new DateOnly(2026, 4, 19);
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260419:Nakayama:9"), new("race-detail"), 1,
            CollectionReason.Recovery, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date, new Dictionary<string, string>());

        Assert.AreEqual(new RaceId(date, RaceCourse.Nakayama, 9),
            JraRaceDetailCollectionHandler.ParseRaceId(task));
    }

    [TestMethod]
    public void RaceDetail_LegacyTaskWithMismatchedResourceDate_IsRejected()
    {
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260420:Nakayama:9"), new("race-detail"), 1,
            CollectionReason.Recovery, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), new DateOnly(2026, 4, 19), new Dictionary<string, string>());

        Assert.Throws<InvalidOperationException>(() => JraRaceDetailCollectionHandler.ParseRaceId(task));
    }

    [TestMethod]
    public async Task RaceDetail_RecentFinishedRace_CollectsCardThenResultInOneTask()
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
        var card = new FakeJraRaceCardCollectionWorkflow();
        var result = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = id => new RaceResultCollectionResult(id, "domain-race", [1], [],
                "https://example.test/result/11", true),
        };
        var handler = new JraRaceDetailCollectionHandler(sessions, _ => card, _ => result,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260912:Tokyo:11"), new("race-detail"), 1,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11", ["startTime"] = "15:30" },
            [new(1, direct, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null)]),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.HasCount(1, card.RefreshRequests);
        Assert.HasCount(1, result.Requests);
        Assert.AreEqual(race, result.Requests.Single());
    }

    [TestMethod]
    public async Task RaceDetail_SubjectBatchRejectionStillCollectsResultAndIsolatesFailure()
    {
        var date = new DateOnly(2026, 9, 19);
        var race = new RaceId(date, RaceCourse.Nakayama, 2);
        var direct = new Uri("https://example.test/card/2");
        var cardPage = new JraRaceCardPage(direct.AbsoluteUri, race, "test", new(10, 30),
            [new RaceEntry(1, "テスト馬", 1, null, 55m,
                HorseSourceIdentity: "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud001234567890/01")]);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator { DirectUrlFactory = _ => cardPage },
        };
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = id => new RaceResultCollectionResult(id, "domain-race", [1], [],
                "https://example.test/result/2", true),
        };
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            requests: new RejectingRequestSink(),
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Nakayama:2"), new("race-detail"), 1,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "中山", ["number"] = "2", ["startTime"] = "10:30" },
            [new(1, direct, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null)]),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ValidationFailure, completion.Result);
        Assert.AreEqual("ReferencedSubjectBatchRejected", completion.ErrorCode);
        Assert.AreEqual(CollectionFailureImpact.Isolated, completion.FailureImpact);
        Assert.HasCount(1, results.Requests);
        StringAssert.Contains(completion.ErrorMessage, "ResourceSuppressed");
    }

    [TestMethod]
    public async Task RaceDetail_CardBoundaryOutOfRange_ContinuesWithResultCollection()
    {
        var today = new DateOnly(2026, 9, 17);
        var date = today.AddDays(-JraNavigator.DefaultRaceCardLookupPeriodDays);
        var race = new RaceId(date, RaceCourse.Nakayama, 11);
        var card = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraNavigationException(
                "card retired",
                JraNavigationFailureReason.OutOfDisplayedRange),
        };
        var result = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = id => new RaceResultCollectionResult(
                id,
                "domain-race",
                [1],
                [],
                "https://example.test/result/11",
                true),
        };
        var handler = new JraRaceDetailCollectionHandler(
            new FakeJraSessionFactory(),
            _ => card,
            _ => result,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 16, 15, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(
            new LeasedCollectionTask(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new(ResourceType.Race, "JRA", "20260912:Nakayama:11"),
                new("race-detail"),
                1,
                CollectionReason.Discovery,
                CollectionLane.Realtime,
                100,
                "lease",
                DateTimeOffset.UtcNow.AddMinutes(5),
                date,
                new Dictionary<string, string> { ["course"] = "中山", ["number"] = "11" }),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.HasCount(1, card.RefreshRequests);
        Assert.HasCount(1, result.Requests);
        Assert.AreEqual(race, result.Requests.Single());
    }

    [TestMethod]
    public async Task RaceDetail_OfficialCancellation_CompletesWithoutWaitingForPayouts()
    {
        var date = new DateOnly(2026, 9, 12);
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = id => new RaceResultCollectionResult(id, "domain-race", [], [],
                "https://example.test/result/11", IsOfficiallyConfirmed: false, IsOfficiallyCancelled: true),
        };
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(),
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(CreateTask(ResourceType.Race, "race-detail", date),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        StringAssert.StartsWith(completion.PageIdentification, "RaceDetailOfficialCancellation:");
    }

    [TestMethod]
    public async Task RaceDetail_ResultNotPublished_IsAvailabilityWaitEvenOutsideCardWindow()
    {
        var date = new DateOnly(2026, 9, 1);
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ThrowOnCollect = new JraNavigationException("result is not published",
                HorseRacingPrediction.Scraping.Jra.Navigation.JraNavigationFailureReason.NotYetPublished),
        };
        var now = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(),
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(now), options: Options.Create(new RaceDetailCollectionOptions
            {
                HistoricalResultInitialRetryMinutes = 7,
                HistoricalResultMaxRetryMinutes = 45,
            }));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260901:Tokyo:11"), new("race-detail"), 1,
            CollectionReason.Backfill, CollectionLane.Background, 10, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11" }),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, completion.Result);
        Assert.AreEqual("RaceResultNotYetAvailable", completion.ErrorCode);
        Assert.AreEqual(now.AddMinutes(45), completion.RetryAt);
    }

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
        var handler = new JraRaceCardCollectionHandler(sessions, _ => workflow,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)));
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
        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)))
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
        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)))
            .CollectAsync(CreateTask(ResourceType.RaceCard, "race-card", date, first, second), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.AreEqual(second, result.RequestedUrl);
        Assert.IsNotNull(result.LocationOutcomes);
        Assert.AreEqual(CollectionAttemptResult.TransientFailure, result.LocationOutcomes[0].Result);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.LocationOutcomes[1].Result);
        CollectionAssert.AreEqual(new[] { first, second }, sessions.LastNavigator!.DirectUrlRequests);
    }

    [TestMethod]
    [DataRow(404, CollectionAttemptResult.ResourceNotFound)]
    [DataRow(429, CollectionAttemptResult.AccessLimited)]
    [DataRow(503, CollectionAttemptResult.TransientFailure)]
    public async Task RaceCard_ClassifiesHttpCandidateFailureAndContinuesWithNextCandidate(
        int statusCode, CollectionAttemptResult expectedResult)
    {
        var date = new DateOnly(2026, 9, 12);
        var first = new Uri($"https://example.test/card/{statusCode}");
        var second = new Uri("https://example.test/card/11");
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => url == first
                    ? throw new HttpRequestException("candidate failed", null,
                        (System.Net.HttpStatusCode)statusCode)
                    : new JraRaceCardPage(url.AbsoluteUri, race, "test", new(15, 30), []),
            },
        };
        var workflow = new FakeJraRaceCardCollectionWorkflow();

        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)))
            .CollectAsync(CreateTask(ResourceType.RaceCard, "race-card", date, first, second),
                CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.AreEqual(expectedResult, result.LocationOutcomes![0].Result);
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
        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)))
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
        var now = new DateTimeOffset(2026, 9, 12, 15, 30, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 9, 13);
        var sessions = new FakeJraSessionFactory();
        var workflow = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraCollectionException("出馬表を取得できませんでした。")
        };
        var task = CreateTask(ResourceType.RaceCard, "race-card", date);

        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow,
                timeProvider: new FixedTimeProvider(now))
            .CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, result.Result);
        Assert.AreEqual("RaceCardNotYetAvailable", result.ErrorCode);
        Assert.IsNotNull(result.RetryAt);
    }

    [TestMethod]
    public async Task RaceCard_DiscoveryReachesWrongPage_IsUnexpectedPageWithDiagnostics()
    {
        var date = new DateOnly(2026, 9, 12);
        const string wrongUrl = "https://example.test/odds/11";
        var sessions = new FakeJraSessionFactory();
        var workflow = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraPageKindMismatchException(
                JraPageKind.RaceCard,
                JraPageKind.RaceOdds,
                wrongUrl,
                "20260912:Tokyo:11"),
        };

        var result = await new JraRaceCardCollectionHandler(sessions, _ => workflow,
                timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero)))
            .CollectAsync(CreateTask(ResourceType.RaceCard, "race-card", date), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.UnexpectedPage, result.Result);
        Assert.AreEqual(nameof(JraPageKindMismatchException), result.ErrorCode);
        Assert.AreEqual(new Uri(wrongUrl), result.FinalUrl);
        Assert.AreEqual(
            "Expected=RaceCard; Actual=RaceOdds; Resource=20260912:Tokyo:11",
            result.PageIdentification);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RejectingRequestSink : ICollectionRequestSink
    {
        public Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
            CollectionReason reason, CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CollectionRequestBulkResponse> RequestManyAsync(CollectionRequestBulkRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new CollectionRequestBulkResponse(
            request.Items.Select(item => new CollectionRequestBulkOutcome(item.ItemKey, "Rejected",
                ErrorCode: "ResourceSuppressed")).ToArray()));
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

    [TestMethod]
    public async Task RaceResult_InvalidCandidate_FallsBackToDiscoveryWorkflow()
    {
        var date = new DateOnly(2026, 9, 12);
        var invalid = new Uri("https://example.test/not-a-result");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => new JraUnknownPage(url.AbsoluteUri, "unexpected"),
            },
        };
        var workflow = new FakeJraRaceResultCollectionWorkflow();

        var result = await new JraRaceResultCollectionHandler(sessions, _ => workflow)
            .CollectAsync(CreateTask(ResourceType.RaceResult, "race-result", date, invalid),
                CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.AreEqual(CollectionAttemptResult.UnexpectedPage, result.LocationOutcomes!.Single().Result);
        Assert.HasCount(1, workflow.Requests);
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

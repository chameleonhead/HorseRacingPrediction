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
    public async Task RaceDetail_ResultDue_CardUnavailable_ContinuesWithResultCollection()
    {
        var date = new DateOnly(2026, 9, 21);
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var card = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraCollectionException("official card is unavailable"),
        };
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = id => new RaceResultCollectionResult(id, "domain-race", [1], [],
                "https://example.test/result/11", true),
        };
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(),
            _ => card, _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260921:Tokyo:11"), new("race-detail"), 2,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11", ["startTime"] = "15:30" }),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.HasCount(1, results.Requests);
        Assert.AreEqual(race, results.Requests.Single());
        Assert.IsTrue(completion.StageOutcomes!.Any(x => x.Stage == "ResolveCard"
            && x.ErrorCode == "OfficialRaceCardUnavailable"
            && x.Result == CollectionAttemptResult.NotApplicable));
    }

    [TestMethod]
    public async Task RaceDetail_ResultNotDue_CardUnavailable_WaitsWithoutNavigatingToResult()
    {
        var date = new DateOnly(2026, 9, 21);
        var card = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraCollectionException("official card is not published"),
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(),
            _ => card, _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 21, 5, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260921:Tokyo:11"), new("race-detail"), 2,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11", ["startTime"] = "15:30" }),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, completion.Result);
        Assert.AreEqual("RaceCardNotYetAvailable", completion.ErrorCode);
        Assert.IsEmpty(results.Requests);
    }

    [TestMethod]
    public async Task RaceDetail_RescheduledMeeting_RequestsReplacementOnceAndEndsOldTask()
    {
        var originalDate = new DateOnly(2026, 9, 21);
        var replacementDate = new DateOnly(2026, 9, 22);
        var original = new RaceId(originalDate, RaceCourse.Nakayama, 2);
        var replacement = original with { Date = replacementDate };
        var oldUrl = new Uri("https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604070220260921/69");
        var newUrl = "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604070220260922/00";
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => throw new JraCollectionException("DB error 007"),
                RaceCardFactory = id => id == replacement
                    ? new JraRaceCardPage(newUrl, replacement, "replacement", new(10, 20), [],
                        MeetingNumber: 4, MeetingDay: 7)
                    : throw new JraNavigationException("not this date"),
            },
        };
        var card = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraCollectionException("official card is unavailable"),
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var sink = new RecordingRequestSink();
        var handler = new JraRaceDetailCollectionHandler(sessions, _ => card, _ => results,
            requests: sink,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260921:Nakayama:2"), new("race-detail"), 2,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), originalDate,
            new Dictionary<string, string> { ["course"] = "中山", ["number"] = "2", ["startTime"] = "10:20", ["domainRaceId"] = "old-domain" },
            [new(1, oldUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null)]),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.NotApplicable, completion.Result);
        Assert.AreEqual("MeetingRescheduled", completion.ErrorCode);
        Assert.HasCount(1, sink.Requests);
        Assert.AreEqual(new ResourceKey(ResourceType.Race, "JRA", "20260922:Nakayama:2"), sink.Requests[0].Resource);
        Assert.AreEqual(CollectionReason.Recovery, sink.Requests[0].Reason);
        Assert.AreEqual(replacementDate, sink.Requests[0].EffectiveDate);
        Assert.AreEqual(original.ToString(), sink.Requests[0].Attributes["rescheduledFrom"]);
        Assert.IsFalse(sink.Requests[0].Attributes.ContainsKey("domainRaceId"));
        Assert.IsEmpty(results.Requests);
    }

    [TestMethod]
    public async Task RaceDetail_CancelledMeetingWithoutReplacement_WaitsForOfficialDecision()
    {
        var date = new DateOnly(2026, 9, 21);
        var oldUrl = new Uri("https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604070220260921/69");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => throw new JraCollectionException("DB error 007"),
                RaceCardFactory = _ => throw new JraNavigationException("not published"),
            },
        };
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ThrowOnCollect = new JraNavigationException("result unavailable"),
        };
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow
            {
                ThrowOnCollect = new JraCollectionException("card unavailable"),
            },
            _ => results,
            requests: new RecordingRequestSink(),
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260921:Nakayama:2"), new("race-detail"), 2,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "中山", ["number"] = "2", ["startTime"] = "10:20" },
            [new(1, oldUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null)]),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, completion.Result);
        Assert.AreEqual("MeetingCancelledAwaitingDecision", completion.ErrorCode);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.Zero), completion.RetryAt);
    }

    [TestMethod]
    public async Task RaceDetail_OwnerRepairWithoutOfficialCard_IsTerminallyUnavailable()
    {
        var date = new DateOnly(2026, 9, 19);
        var card = new FakeJraRaceCardCollectionWorkflow
        {
            ThrowOnCollect = new JraCollectionException("出馬表を取得できませんでした。"),
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(),
            _ => card, _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:11"), new("race-detail"), 2,
            CollectionReason.DefinitionChanged, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string>
            {
                ["course"] = "東京",
                ["number"] = "11",
                ["ownerRepair"] = "true",
            }), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.NotApplicable, completion.Result);
        Assert.AreEqual("OfficialRaceCardUnavailable", completion.ErrorCode);
        Assert.IsNull(completion.RetryAt);
        Assert.IsTrue(completion.StageOutcomes!.Any(x => x.Stage == "ResolveCard"
            && x.Result == CollectionAttemptResult.NotApplicable));
        Assert.IsEmpty(results.Requests);
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
    public async Task RaceDetail_MissingOfficialStartTime_DoesNotNavigateToResult()
    {
        var date = new DateOnly(2026, 9, 19);
        var now = new DateTimeOffset(2026, 9, 19, 1, 0, 0, TimeSpan.Zero);
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(),
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(now));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            now.AddMinutes(5), date, new Dictionary<string, string>
            {
                ["course"] = "東京",
                ["number"] = "1",
            }), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, completion.Result);
        Assert.AreEqual("OfficialStartTimeUnknown", completion.ErrorCode);
        Assert.IsEmpty(results.Requests);
        Assert.IsNotNull(completion.StageOutcomes);
        Assert.IsTrue(completion.StageOutcomes.Any(x => x.Stage == "AwaitOfficialStart"));
    }

    [TestMethod]
    public async Task RaceDetail_CardWriteRejection_IsPreservedAlongsideResultOutcome()
    {
        var date = new DateOnly(2026, 9, 19);
        var card = new FakeJraRaceCardCollectionWorkflow { OutcomeError = "write rejected" };
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = id => new RaceResultCollectionResult(id, "domain-race", [1], [],
                "https://example.test/result/1", true),
        };
        var handler = new JraRaceDetailCollectionHandler(new FakeJraSessionFactory(), _ => card, _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date, new Dictionary<string, string>
            {
                ["course"] = "東京",
                ["number"] = "1",
                ["startTime"] = "10:00",
            }), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ValidationFailure, completion.Result);
        Assert.AreEqual("RaceCardWriteRejected", completion.ErrorCode);
        Assert.AreEqual(CollectionFailureImpact.Isolated, completion.FailureImpact);
        Assert.IsNotNull(completion.StageOutcomes);
        Assert.IsTrue(completion.StageOutcomes.Any(x => x.Artifact == RaceArtifactKind.Card
            && x.ErrorCode == "RaceCardWriteRejected"));
        Assert.IsTrue(completion.StageOutcomes.Any(x => x.Artifact == RaceArtifactKind.Result && x.Persisted));
    }

    [TestMethod]
    public async Task RaceDetail_CardWriteRejection_PreservesParsedStartEvidenceAndClassifiesLocation()
    {
        var date = new DateOnly(2026, 9, 19);
        var race = new RaceId(date, RaceCourse.Tokyo, 1);
        var url = new Uri("https://example.test/card/1");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => new JraRaceCardPage(url.AbsoluteUri, race, "test", new(10, 0), [])
            }
        };
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow { OutcomeError = "write rejected" },
            _ => new FakeJraRaceResultCollectionWorkflow(),
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.ManualRefresh, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "1" },
            [new(1, url, ResourceLocationSource.Discovered, ResourceLocationStatus.Unknown, null)]),
            CancellationToken.None);

        Assert.IsNotNull(completion.RaceEvidence);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.FromHours(9)),
            completion.RaceEvidence.OfficialStartAt);
        Assert.AreEqual(RaceArtifactKind.Card, completion.LocationOutcomes!.Single().Artifact);
    }

    [TestMethod]
    public async Task RaceDetail_CurrentCardStartTimeSupersedesLeasedOfficialStart()
    {
        var date = new DateOnly(2026, 9, 19);
        var race = new RaceId(date, RaceCourse.Tokyo, 1);
        var url = new Uri("https://example.test/card/1");
        var now = new DateTimeOffset(2026, 9, 19, 1, 10, 0, TimeSpan.Zero);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => new JraRaceCardPage(url.AbsoluteUri, race, "test", new(10, 30), [])
            }
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(now));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease", now.AddMinutes(5), date,
            new Dictionary<string, string>
            {
                ["course"] = "東京",
                ["number"] = "1",
                ["officialStartAt"] = "2026-09-19T01:00:00+00:00",
            },
            [new(1, url, ResourceLocationSource.Discovered, ResourceLocationStatus.Unknown, null,
                RaceArtifactKind.Card)]), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, completion.Result);
        Assert.AreEqual("RaceNotStarted", completion.ErrorCode);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 19, 1, 35, 0, TimeSpan.Zero), completion.RetryAt);
        Assert.IsEmpty(results.Requests);
    }

    [TestMethod]
    public async Task RaceDetail_ResultStage_DoesNotNavigateToCardClassifiedLocation()
    {
        var date = new DateOnly(2026, 9, 19);
        var cardUrl = new Uri("https://example.test/card/1");
        var sessions = new FakeJraSessionFactory();
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = id => new(id, "domain-race", [1], [], "https://example.test/result/1", true)
        };
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.ManualRefresh, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date, new Dictionary<string, string>
            {
                ["course"] = "東京",
                ["number"] = "1",
                ["cardArtifactStatus"] = "Current",
                ["officialStartAt"] = "2026-09-19T10:00:00+09:00"
            }, [new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null,
                RaceArtifactKind.Card)]), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsEmpty(sessions.LastNavigator!.DirectUrlRequests);
        Assert.HasCount(1, results.Requests);
    }

    [TestMethod]
    public async Task RaceDetail_AfterCollectingCard_UsesResultClassifiedLocationBeforeFallback()
    {
        var date = new DateOnly(2026, 9, 19);
        var race = new RaceId(date, RaceCourse.Tokyo, 1);
        var cardUrl = new Uri("https://example.test/card/1");
        var resultUrl = new Uri("https://example.test/result/1");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => url == cardUrl
                    ? new JraRaceCardPage(url.AbsoluteUri, race, "test", new(10, 0), [])
                    : new JraRaceResultPage(url.AbsoluteUri, race, "test", []),
            },
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.Discovery, CollectionLane.Realtime, 100, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "1" },
            [
                new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null,
                    RaceArtifactKind.Card),
                new(2, resultUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null,
                    RaceArtifactKind.Result),
            ]), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        CollectionAssert.AreEqual(new[] { cardUrl, resultUrl }, sessions.LastNavigator!.DirectUrlRequests);
        Assert.HasCount(1, results.RefreshPageRequests);
        Assert.IsEmpty(results.Requests);
    }

    [TestMethod]
    public async Task RaceDetail_CurrentResult_SkipsResultNavigationAfterCardRepair()
    {
        var date = new DateOnly(2026, 9, 19);
        var race = new RaceId(date, RaceCourse.Tokyo, 1);
        var cardUrl = new Uri("https://example.test/card/1");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = url => new JraRaceCardPage(url.AbsoluteUri, race, "test", new(10, 0), []),
            },
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.DefinitionChanged, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string>
            {
                ["course"] = "東京",
                ["number"] = "1",
                ["resultArtifactStatus"] = "Current",
            },
            [new(1, cardUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null,
                RaceArtifactKind.Card)]), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, completion.Result);
        Assert.IsEmpty(results.Requests);
        Assert.IsEmpty(results.RefreshPageRequests);
        Assert.IsFalse(completion.StageOutcomes!.Any(x => x.Artifact == RaceArtifactKind.Result));
    }

    [TestMethod]
    [DataRow(429, CollectionAttemptResult.AccessLimited)]
    [DataRow(503, CollectionAttemptResult.TransientFailure)]
    public async Task RaceDetail_ResultCandidateAccessFailure_StopsBeforeFallback(
        int statusCode, CollectionAttemptResult expected)
    {
        var date = new DateOnly(2026, 9, 19);
        var resultUrl = new Uri($"https://example.test/result/{statusCode}");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => throw new HttpRequestException("candidate failed", null,
                    (System.Net.HttpStatusCode)statusCode),
            },
        };
        var results = new FakeJraRaceResultCollectionWorkflow();
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.Recovery, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string>
            {
                ["course"] = "東京",
                ["number"] = "1",
                ["cardArtifactStatus"] = "Current",
                ["officialStartAt"] = "2026-09-19T10:00:00+09:00",
            },
            [new(1, resultUrl, ResourceLocationSource.Discovered, ResourceLocationStatus.Active, null,
                RaceArtifactKind.Result)]), CancellationToken.None);

        Assert.AreEqual(expected, completion.Result);
        Assert.HasCount(1, sessions.LastNavigator!.DirectUrlRequests);
        Assert.IsEmpty(results.Requests);
    }

    [TestMethod]
    public async Task RaceDetail_MissingCardOwner_IsStructuredIsolatedFailureAfterResultPersists()
    {
        var date = new DateOnly(2026, 9, 19);
        var race = new RaceId(date, RaceCourse.Tokyo, 1);
        var url = new Uri("https://example.test/card/1");
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                DirectUrlFactory = _ => new JraRaceCardPage(url.AbsoluteUri, race, "test", new(10, 0),
                    [new RaceEntry(1, "Owner Missing", 1, "Jockey", 55m)])
            }
        };
        var results = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = id => new(id, "domain-race", [1], [], "https://example.test/result/1", true)
        };
        var handler = new JraRaceDetailCollectionHandler(sessions,
            _ => new FakeJraRaceCardCollectionWorkflow(), _ => results,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero)));

        var completion = await handler.CollectAsync(new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "20260919:Tokyo:1"), new("race-detail"), 2,
            CollectionReason.ManualRefresh, CollectionLane.Normal, 50, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "1" },
            [new(1, url, ResourceLocationSource.Discovered, ResourceLocationStatus.Unknown, null)]),
            CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ValidationFailure, completion.Result);
        Assert.AreEqual("RaceCardOwnerIncomplete", completion.ErrorCode);
        Assert.AreEqual(CollectionFailureImpact.Isolated, completion.FailureImpact);
        Assert.IsTrue(completion.StageOutcomes!.Any(x => x.Stage == "ValidateCardOwners"
            && x.ErrorCode == "RaceCardOwnerIncomplete"));
        Assert.IsTrue(completion.StageOutcomes!.Any(x => x.Stage == "PersistResult" && x.Persisted));
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
    public async Task RaceDetail_ResultDue_CardReachesWrongPage_RecordsDiagnosticsAndContinuesResult()
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

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        var stageOutcomes = result.StageOutcomes!;
        Assert.IsTrue(stageOutcomes.Any(x => x.Stage == "ResolveCard"
            && x.Result == CollectionAttemptResult.UnexpectedPage
            && x.ErrorCode == nameof(JraPageKindMismatchException)
            && x.FinalUrl == new Uri(wrongUrl)));
        Assert.IsTrue(stageOutcomes.Any(x => x.Artifact == RaceArtifactKind.Result && x.Persisted));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingRequestSink : ICollectionRequestSink
    {
        public List<(ResourceKey Resource, CollectionReason Reason, DateOnly EffectiveDate,
            IReadOnlyDictionary<string, string> Attributes)> Requests
        { get; } = [];

        public Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
            CollectionReason reason, CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
        {
            Requests.Add((resource, reason, effectiveDate, attributes));
            return Task.CompletedTask;
        }
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
        new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11", ["startTime"] = "15:30" },
        locations.Select((url, index) => new ResourceLocationCandidate(index, url,
            ResourceLocationSource.Discovered, ResourceLocationStatus.Unknown, null)).ToArray());
}

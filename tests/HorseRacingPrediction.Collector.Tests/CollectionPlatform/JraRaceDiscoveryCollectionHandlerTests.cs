using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraRaceDiscoveryCollectionHandlerTests
{
    [TestMethod]
    public async Task Discovery_ExpandsActualRaceListIntoNewPlatformRequests()
    {
        var date = new DateOnly(2026, 9, 12);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => new JraRaceListPage("https://example.test/list", target,
                    course, [new(new(target, course, 11), "test", new(15, 30),
                        "https://example.test/card/11", "https://example.test/result/11")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target => target == date ? [RaceCourse.Tokyo] : [],
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink);
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "discovery:2026091200"), new("race-discovery"), 1,
            CollectionReason.Discovery, CollectionLane.Realtime, 70, "lease", DateTimeOffset.UtcNow.AddMinutes(5),
            date, new Dictionary<string, string>());

        var result = await handler.CollectAsync(task, CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.Succeeded, result.Result);
        Assert.HasCount(3, sink.Requests);
        Assert.IsTrue(sink.Requests.Any(x => x.Resource.Type == ResourceType.RaceCard));
        Assert.IsTrue(sink.Requests.Any(x => x.Resource.Type == ResourceType.RaceResult));
        Assert.IsTrue(sink.Requests.Any(x => x.Resource.Type == ResourceType.RaceOdds));
        Assert.IsTrue(sink.Requests.All(x => x.Resource.Id == "20260912:Tokyo:11"));
    }

    [TestMethod]
    public async Task BackfillDiscovery_VisitsOnlyItsDayAndPropagatesBatchId()
    {
        var date = new DateOnly(2020, 1, 5);
        var visited = new List<DateOnly>();
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceResultListFactory = (target, course) => new JraRaceResultPage(
                    "https://example.test/result/1", new(target, course, 1), "race", []),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target =>
            {
                visited.Add(target);
                return target == date ? [RaceCourse.Tokyo] : [];
            },
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink);
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.Race, "JRA", "backfill:20200105"), new("race-discovery"), 1,
            CollectionReason.Backfill, CollectionLane.Background, 10, "lease", DateTimeOffset.UtcNow.AddMinutes(5),
            date, new Dictionary<string, string> { ["batchId"] = "jra:2020-01" });

        await handler.CollectAsync(task, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { date }, visited);
        Assert.IsTrue(sink.Requests.All(x => x.Attributes.GetValueOrDefault("batchId") == "jra:2020-01"));
    }

    [TestMethod]
    public async Task FutureUnpublishedRaceList_ReturnsWaitingAndKeepsRequestsAlreadyDiscovered()
    {
        var today = new DateOnly(2026, 9, 12);
        var future = new DateOnly(2026, 9, 19);
        var now = new DateTimeOffset(2026, 9, 12, 1, 0, 0, TimeSpan.Zero); // 10:00 JST
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => target == future
                    ? throw new JraNavigationException("not published", JraNavigationFailureReason.NotYetPublished)
                    : new JraRaceListPage("https://example.test/list", target, course,
                        [new(new(target, course, 11), "test", new(15, 30),
                            "https://example.test/card/11", "https://example.test/result/11")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target => target == today || target == future ? [RaceCourse.Tokyo] : [],
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            Options.Create(new RaceDiscoveryCollectionOptions()), new FixedTimeProvider(now));

        var result = await handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, result.Result);
        Assert.AreEqual("RaceListNotYetAvailable", result.ErrorCode);
        Assert.IsNotNull(result.RetryAt);
        Assert.IsGreaterThan(now, result.RetryAt.Value);
        Assert.HasCount(3, sink.Requests);
        Assert.IsTrue(sink.Requests.All(x => x.Resource.Id.StartsWith("20260912", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task TodayUnexpectedPage_IsNotClassifiedAsWaiting()
    {
        var today = new DateOnly(2026, 9, 12);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => new JraUnknownPage("https://example.test/unexpected", "unexpected"),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == today ? [RaceCourse.Tokyo] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            Options.Create(new RaceDiscoveryCollectionOptions()),
            new FixedTimeProvider(new(2026, 9, 11, 15, 30, 0, TimeSpan.Zero))); // 9/12 00:30 JST

        await Assert.ThrowsExactlyAsync<JraCollectionException>(() =>
            handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None));
    }

    [TestMethod]
    public async Task FutureHttpFailure_IsNotMisclassifiedAsPublicationWaiting()
    {
        var today = new DateOnly(2026, 9, 12);
        var future = today.AddDays(7);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new HttpRequestException("unavailable", null,
                    System.Net.HttpStatusCode.ServiceUnavailable),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == future ? [RaceCourse.Tokyo] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            Options.Create(new RaceDiscoveryCollectionOptions()),
            new FixedTimeProvider(new(2026, 9, 12, 1, 0, 0, TimeSpan.Zero)));

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None));
        Assert.AreEqual(System.Net.HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [TestMethod]
    public async Task FutureParseFailure_IsNotMisclassifiedAsPublicationWaiting()
    {
        var today = new DateOnly(2026, 9, 12);
        var future = today.AddDays(7);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraPageParseException(
                    JraPageKind.RaceList, "https://example.test/broken", "broken table"),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == future ? [RaceCourse.Tokyo] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            Options.Create(new RaceDiscoveryCollectionOptions()),
            new FixedTimeProvider(new(2026, 9, 12, 1, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsExactlyAsync<JraPageParseException>(() =>
            handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None));
    }

    [TestMethod]
    public async Task UnpublishedDay_DoesNotPreventDiscoveryOfLaterPublishedDay()
    {
        var today = new DateOnly(2026, 9, 12);
        var unpublished = today.AddDays(6);
        var published = today.AddDays(7);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (target, course) => target == unpublished
                    ? throw new JraNavigationException("not published", JraNavigationFailureReason.NotYetPublished)
                    : new JraRaceListPage("https://example.test/list", target, course,
                        [new(new(target, course, 11), "published", new(15, 30),
                            "https://example.test/card/11", "https://example.test/result/11")]),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = target => target == unpublished || target == published ? [RaceCourse.Tokyo] : [],
        };
        var sink = new RecordingSink();
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, sink,
            Options.Create(new RaceDiscoveryCollectionOptions()),
            new FixedTimeProvider(new(2026, 9, 12, 1, 0, 0, TimeSpan.Zero)));

        var result = await handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, result.Result);
        Assert.HasCount(3, sink.Requests);
        Assert.IsTrue(sink.Requests.All(x => x.Resource.Id.StartsWith("20260919", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task TomorrowUnpublished_UsesConfiguredNearPublicationInterval()
    {
        var today = new DateOnly(2026, 9, 12);
        var tomorrow = today.AddDays(1);
        var now = new DateTimeOffset(2026, 9, 12, 1, 0, 0, TimeSpan.Zero);
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceCardListFactory = (_, _) => throw new JraNavigationException(
                    "not published", JraNavigationFailureReason.NotYetPublished),
            },
        };
        var schedule = new FakeJraScheduleCollectionWorkflow
        { CoursesByDate = target => target == tomorrow ? [RaceCourse.Tokyo] : [] };
        var handler = new JraRaceDiscoveryCollectionHandler(sessions, _ => schedule, new RecordingSink(),
            Options.Create(new RaceDiscoveryCollectionOptions { NearPublicationRetryMinutes = 47 }),
            new FixedTimeProvider(now));

        var result = await handler.CollectAsync(CreateDiscoveryTask(today), CancellationToken.None);

        Assert.AreEqual(CollectionAttemptResult.ResourceNotYetAvailable, result.Result);
        Assert.AreEqual(now.AddMinutes(47), result.RetryAt);
    }

    private static LeasedCollectionTask CreateDiscoveryTask(DateOnly date) => new(Guid.NewGuid(), Guid.NewGuid(),
        new(ResourceType.Race, "JRA", $"discovery:{date:yyyyMMdd}00"), new("race-discovery"), 1,
        CollectionReason.Discovery, CollectionLane.Realtime, 70, "lease", DateTimeOffset.UtcNow.AddMinutes(5),
        date, new Dictionary<string, string>());

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingSink : ICollectionRequestSink
    {
        public List<(ResourceKey Resource, CollectionDefinitionId Definition,
            IReadOnlyDictionary<string, string> Attributes)> Requests
        { get; } = [];
        public Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, CollectionReason reason,
            CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
            IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
        {
            Requests.Add((resource, definition, attributes));
            return Task.CompletedTask;
        }
    }
}

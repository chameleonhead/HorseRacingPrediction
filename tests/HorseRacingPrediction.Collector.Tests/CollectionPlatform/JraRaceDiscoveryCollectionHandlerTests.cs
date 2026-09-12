using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

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

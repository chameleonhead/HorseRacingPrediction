using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
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
    }
}

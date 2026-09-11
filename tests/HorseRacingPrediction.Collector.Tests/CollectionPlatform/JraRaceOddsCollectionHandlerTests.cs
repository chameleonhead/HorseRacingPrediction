using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraRaceOddsCollectionHandlerTests
{
    [TestMethod]
    public async Task RepeatedCollection_AppendsDistinctObservedSnapshotsAndSchedulesNextObservation()
    {
        var date = new DateOnly(2026, 9, 12);
        var race = new RaceId(date, RaceCourse.Tokyo, 11);
        var calls = 0;
        var sessions = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceOddsFactory = _ =>
                {
                    calls++;
                    return new JraRaceOddsPage("https://example.test/odds/11", race,
                        new DateTimeOffset(2026, 9, 12, 5, calls, 0, TimeSpan.Zero),
                        [new(1, calls == 1 ? 2.5m : 2.3m, 1)]);
                },
            },
        };
        var sink = new RecordingSink();
        var handler = new JraRaceOddsCollectionHandler(sessions, sink,
            Options.Create(new RaceOddsCollectionOptions { EarlyIntervalMinutes = 7 }));
        var task = new LeasedCollectionTask(Guid.NewGuid(), Guid.NewGuid(),
            new(ResourceType.RaceOdds, "JRA", "20260912:Tokyo:11"), new("race-odds"), 1,
            CollectionReason.Discovery, CollectionLane.Realtime, 90, "lease",
            DateTimeOffset.UtcNow.AddMinutes(5), date,
            new Dictionary<string, string> { ["course"] = "東京", ["number"] = "11", ["startTime"] = "15:00" });

        var first = await handler.CollectAsync(task, CancellationToken.None);
        var second = await handler.CollectAsync(task with { TaskId = Guid.NewGuid() }, CancellationToken.None);

        Assert.HasCount(2, sink.Pages);
        Assert.AreNotEqual(sink.Pages[0].ObservedAt, sink.Pages[1].ObservedAt);
        Assert.AreEqual(2.5m, sink.Pages[0].Entries[0].WinOdds);
        Assert.AreEqual(2.3m, sink.Pages[1].Entries[0].WinOdds);
        Assert.IsNotNull(first.NextCollectionAt);
        Assert.IsNotNull(second.NextCollectionAt);
    }

    private sealed class RecordingSink : IRaceOddsSnapshotSink
    {
        public List<JraRaceOddsPage> Pages { get; } = [];
        public Task SaveAsync(string raceId, JraRaceOddsPage page, CancellationToken cancellationToken)
        { Pages.Add(page); return Task.CompletedTask; }
    }
}

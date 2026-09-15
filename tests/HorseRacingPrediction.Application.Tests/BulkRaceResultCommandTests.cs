using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Extensions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Races;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Application.Tests;

[TestClass]
public sealed class BulkRaceResultCommandTests
{
    [TestMethod]
    public async Task OneCommand_PersistsCompleteRaceEnvelope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventFlow(options =>
        {
            options.AddDefaults(typeof(RaceAggregate).Assembly);
            options.AddDefaults(typeof(ApplyBulkRaceResultCommand).Assembly);
        });
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<ICommandBus>();
        var store = provider.GetRequiredService<IAggregateStore>();
        var raceId = RaceId.New;
        var observedAt = new DateTimeOffset(2026, 9, 15, 15, 30, 0, TimeSpan.FromHours(9));
        var data = new BulkRaceResultData(
            new DateOnly(2026, 9, 15), "NAKAYAMA", 11, "Collected race", 1,
            "G1", "TURF", 2000, "RIGHT",
            [new("entry-1", "horse-1", 1, "jockey-1", "trainer-1", 1, 56m, "M", 4, 480m, 2m, null)],
            [new("entry-1", 1, "1:59.9", null, "34.0", null, 1000m, null)],
            "Horse One", observedAt,
            new(observedAt, [new("1", 250m)], [], [], [], []),
            new(observedAt, "SUNNY", "晴", 25m, 50m, "N", 2m),
            new(observedAt, "GOOD", null, "良"));

        var outcome = await bus.PublishAsync(new ApplyBulkRaceResultCommand(raceId, data), CancellationToken.None);

        Assert.IsTrue(outcome.IsSuccess);
        var aggregate = await store.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual(RaceStatus.PayoutDeclared, details.Status);
        Assert.AreEqual(1, details.Entries.Count);
        Assert.AreEqual(1, details.EntryResults.Count);
        Assert.AreEqual("Horse One", details.WinningHorseName);
        Assert.IsNotNull(details.PayoutResult);
    }
}

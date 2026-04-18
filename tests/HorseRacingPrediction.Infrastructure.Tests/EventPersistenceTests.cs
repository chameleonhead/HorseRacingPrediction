using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Commands;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Infrastructure.Tests;

[TestClass]
public class EventPersistenceTests
{
    private ServiceProvider _serviceProvider = null!;
    private ICommandBus _commandBus = null!;
    private IAggregateStore _aggregateStore = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var dbContextProvider = new SqliteDbContextProvider("DataSource=:memory:");
        services.AddSingleton(dbContextProvider);
        services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(dbContextProvider);

        services.AddEventFlow(options =>
        {
            options
                .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
                .AddDefaults(typeof(RaceAggregate).Assembly)
                .UseEntityFrameworkEventStore<EventStoreDbContext>();
        });
        _serviceProvider = services.BuildServiceProvider();
        _commandBus = _serviceProvider.GetRequiredService<ICommandBus>();
        _aggregateStore = _serviceProvider.GetRequiredService<IAggregateStore>();
    }

    [TestCleanup]
    public void Cleanup() => _serviceProvider.Dispose();

    [TestMethod]
    public async Task Race_EventsArePersistedAndLoadedCorrectly()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"),
            CancellationToken.None);

        var loaded = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(
            raceId, CancellationToken.None);

        var details = loaded.GetDetails();
        Assert.AreEqual("皐月賞", details.RaceName);
        Assert.AreEqual(RaceStatus.Draft, details.Status);
    }

    [TestMethod]
    public async Task Race_MultipleEventsArePersistedCorrectly()
    {
        var raceId = RaceId.New;
        var declaredAt = DateTimeOffset.UtcNow;

        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new PublishRaceCardCommand(raceId, 16),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new DeclareRaceResultCommand(raceId, "イクイノックス", declaredAt),
            CancellationToken.None);

        var loaded = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(
            raceId, CancellationToken.None);

        var details = loaded.GetDetails();
        Assert.AreEqual(RaceStatus.ResultDeclared, details.Status);
        Assert.AreEqual("イクイノックス", details.WinningHorseName);
        Assert.AreEqual(16, details.EntryCount);
    }

    [TestMethod]
    public async Task PredictionTicket_EventsArePersistedAndLoadedCorrectly()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.85m, "高確率予想"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new AddPredictionMarkCommand(ticketId, "entry-1", "◎", 1, 95.0m, "本命"),
            CancellationToken.None);

        var loaded = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(
            ticketId, CancellationToken.None);

        var details = loaded.GetDetails();
        Assert.AreEqual("race-abc", details.RaceId);
        Assert.AreEqual(0.85m, details.ConfidenceScore);
        Assert.AreEqual(1, details.Marks.Count);
        Assert.AreEqual("entry-1", details.Marks.First().EntryId);
    }

    [TestMethod]
    public async Task MultipleAggregates_ArePersistedIndependently()
    {
        var raceId1 = RaceId.New;
        var raceId2 = RaceId.New;

        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId1, new DateOnly(2025, 6, 1), "TOKYO", 1, "東京優駿"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId2, new DateOnly(2025, 10, 26), "TOKYO", 11, "天皇賞"),
            CancellationToken.None);

        var loaded1 = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId1, CancellationToken.None);
        var loaded2 = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId2, CancellationToken.None);

        Assert.AreEqual("東京優駿", loaded1.GetDetails().RaceName);
        Assert.AreEqual("天皇賞", loaded2.GetDetails().RaceName);
    }
}

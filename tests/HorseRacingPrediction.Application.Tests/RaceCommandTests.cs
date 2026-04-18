using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Commands;
using EventFlow.Extensions;
using HorseRacingPrediction.Domain.Races;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Application.Tests;

[TestClass]
public class RaceCommandTests
{
    private ServiceProvider _serviceProvider = null!;
    private ICommandBus _commandBus = null!;
    private IAggregateStore _aggregateStore = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventFlow(options =>
        {
            options.AddDefaults(typeof(RaceAggregate).Assembly);
        });
        _serviceProvider = services.BuildServiceProvider();
        _commandBus = _serviceProvider.GetRequiredService<ICommandBus>();
        _aggregateStore = _serviceProvider.GetRequiredService<IAggregateStore>();
    }

    [TestCleanup]
    public void Cleanup() => _serviceProvider.Dispose();

    [TestMethod]
    public async Task CreateRace_Succeeds()
    {
        var raceId = RaceId.New;
        var command = new CreateRaceCommand(
            raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        var result = await _commandBus.PublishAsync(command, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(
            raceId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual(RaceStatus.Draft, details.Status);
        Assert.AreEqual("皐月賞", details.RaceName);
    }

    [TestMethod]
    public async Task PublishCard_AfterCreate_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new PublishRaceCardCommand(raceId, 18),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(
            raceId, CancellationToken.None);
        Assert.AreEqual(RaceStatus.CardPublished, aggregate.GetDetails().Status);
        Assert.AreEqual(18, aggregate.GetDetails().EntryCount);
    }

    [TestMethod]
    public async Task DeclareResult_AfterPublishCard_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new PublishRaceCardCommand(raceId, 18),
            CancellationToken.None);
        var declaredAt = DateTimeOffset.UtcNow;

        var result = await _commandBus.PublishAsync(
            new DeclareRaceResultCommand(raceId, "ディープインパクト", declaredAt),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(
            raceId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual(RaceStatus.ResultDeclared, details.Status);
        Assert.AreEqual("ディープインパクト", details.WinningHorseName);
    }

    [TestMethod]
    public async Task FullLifecycle_ProducesCorrectState()
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

        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(
            raceId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual("有馬記念", details.RaceName);
        Assert.AreEqual(RaceStatus.ResultDeclared, details.Status);
        Assert.AreEqual(16, details.EntryCount);
        Assert.AreEqual("イクイノックス", details.WinningHorseName);
    }
}

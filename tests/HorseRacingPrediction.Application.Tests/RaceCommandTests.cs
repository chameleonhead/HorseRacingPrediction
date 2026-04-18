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
    public async Task RegisterEntry_AfterPublishCard_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new PublishRaceCardCommand(raceId, 18),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new RegisterEntryCommand(raceId, "entry-1", "horse-1", 1, jockeyId: "jockey-1"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().Entries.Count);
    }

    [TestMethod]
    public async Task RecordWeatherObservation_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"),
            CancellationToken.None);
        var observedAt = DateTimeOffset.UtcNow;

        var result = await _commandBus.PublishAsync(
            new RecordWeatherObservationCommand(raceId, observedAt, weatherCode: "SUNNY"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().WeatherObservations.Count);
    }

    [TestMethod]
    public async Task RecordTrackConditionObservation_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"),
            CancellationToken.None);
        var observedAt = DateTimeOffset.UtcNow;

        var result = await _commandBus.PublishAsync(
            new RecordTrackConditionObservationCommand(raceId, observedAt, turfConditionCode: "GOOD"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().TrackConditionObservations.Count);
    }

    [TestMethod]
    public async Task OpenPreRace_AfterPublishCard_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"), CancellationToken.None);
        await _commandBus.PublishAsync(new PublishRaceCardCommand(raceId, 18), CancellationToken.None);

        var result = await _commandBus.PublishAsync(new OpenPreRaceCommand(raceId), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual(RaceStatus.PreRaceOpen, aggregate.GetDetails().Status);
    }

    [TestMethod]
    public async Task StartRace_AfterOpenPreRace_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"), CancellationToken.None);
        await _commandBus.PublishAsync(new PublishRaceCardCommand(raceId, 18), CancellationToken.None);
        await _commandBus.PublishAsync(new OpenPreRaceCommand(raceId), CancellationToken.None);

        var result = await _commandBus.PublishAsync(new StartRaceCommand(raceId), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual(RaceStatus.InProgress, aggregate.GetDetails().Status);
    }

    [TestMethod]
    public async Task DeclareEntryResult_AfterRaceResult_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"), CancellationToken.None);
        await _commandBus.PublishAsync(new PublishRaceCardCommand(raceId, 18), CancellationToken.None);
        await _commandBus.PublishAsync(new DeclareRaceResultCommand(raceId, "ディープインパクト", DateTimeOffset.UtcNow), CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new DeclareEntryResultCommand(raceId, "entry-1", finishPosition: 1, officialTime: "2:00.5"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().EntryResults.Count);
    }

    [TestMethod]
    public async Task DeclarePayoutResult_AfterRaceResult_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"), CancellationToken.None);
        await _commandBus.PublishAsync(new PublishRaceCardCommand(raceId, 18), CancellationToken.None);
        await _commandBus.PublishAsync(new DeclareRaceResultCommand(raceId, "ディープインパクト", DateTimeOffset.UtcNow), CancellationToken.None);

        var winPayouts = new[] { new PayoutEntry("1", 500m) };
        var result = await _commandBus.PublishAsync(
            new DeclarePayoutResultCommand(raceId, DateTimeOffset.UtcNow, winPayouts: winPayouts),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual(RaceStatus.PayoutDeclared, aggregate.GetDetails().Status);
    }

    [TestMethod]
    public async Task CloseRaceLifecycle_AfterPayout_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"), CancellationToken.None);
        await _commandBus.PublishAsync(new PublishRaceCardCommand(raceId, 18), CancellationToken.None);
        await _commandBus.PublishAsync(new DeclareRaceResultCommand(raceId, "ディープインパクト", DateTimeOffset.UtcNow), CancellationToken.None);
        await _commandBus.PublishAsync(new DeclarePayoutResultCommand(raceId, DateTimeOffset.UtcNow, winPayouts: new[] { new PayoutEntry("1", 500m) }), CancellationToken.None);

        var result = await _commandBus.PublishAsync(new CloseRaceLifecycleCommand(raceId), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual(RaceStatus.Closed, aggregate.GetDetails().Status);
    }

    [TestMethod]
    public async Task CorrectRaceData_Succeeds()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(new CreateRaceCommand(raceId, new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"), CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new CorrectRaceDataCommand(raceId, raceName: "日本ダービー", reason: "名称修正"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        Assert.AreEqual("日本ダービー", aggregate.GetDetails().RaceName);
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

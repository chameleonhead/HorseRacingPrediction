using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Commands;
using EventFlow.Extensions;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Application.Tests;

[TestClass]
public class PredictionCommandTests
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
            options.AddDefaults(typeof(CreatePredictionTicketCommand).Assembly);
        });
        _serviceProvider = services.BuildServiceProvider();
        _commandBus = _serviceProvider.GetRequiredService<ICommandBus>();
        _aggregateStore = _serviceProvider.GetRequiredService<IAggregateStore>();
    }

    [TestCleanup]
    public void Cleanup() => _serviceProvider.Dispose();

    [TestMethod]
    public async Task CreatePredictionTicket_Succeeds()
    {
        var ticketId = PredictionTicketId.New;
        var command = new CreatePredictionTicketCommand(
            ticketId, "race-abc", "AI", "model-v1", 0.85m, "高確率予想");

        var result = await _commandBus.PublishAsync(command, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(
            ticketId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual("race-abc", details.RaceId);
        Assert.AreEqual("AI", details.PredictorType);
        Assert.AreEqual(0.85m, details.ConfidenceScore);
    }

    [TestMethod]
    public async Task AddMark_AfterCreate_Succeeds()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.85m, null),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new AddPredictionMarkCommand(ticketId, "entry-1", "◎", 1, 90.5m, "本命"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(
            ticketId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().Marks.Count);
    }

    [TestMethod]
    public async Task AddBettingSuggestion_Succeeds()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.85m, null),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new AddBettingSuggestionCommand(ticketId, "WIN", "1", stakeAmount: 1000m),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(ticketId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().BettingSuggestions.Count);
    }

    [TestMethod]
    public async Task AddRationale_Succeeds()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.85m, null),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new AddPredictionRationaleCommand(ticketId, "Horse", "horse-1", "SpeedFigure", "95"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(ticketId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().Rationales.Count);
    }

    [TestMethod]
    public async Task FinalizeTicket_Succeeds()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.85m, null),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new FinalizePredictionTicketCommand(ticketId),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(ticketId, CancellationToken.None);
        Assert.AreEqual(TicketStatus.Finalized, aggregate.GetDetails().TicketStatus);
    }

    [TestMethod]
    public async Task WithdrawTicket_Succeeds()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.85m, null),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new WithdrawPredictionTicketCommand(ticketId, reason: "予想変更"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(ticketId, CancellationToken.None);
        Assert.AreEqual(TicketStatus.Withdrawn, aggregate.GetDetails().TicketStatus);
    }

    [TestMethod]
    public async Task EvaluateTicket_Succeeds()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.85m, null),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new EvaluatePredictionTicketCommand(ticketId, "race-abc", DateTimeOffset.UtcNow, 1, new[] { "WIN" }),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(ticketId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().Evaluations.Count);
    }

    [TestMethod]
    public async Task CorrectMetadata_Succeeds()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.85m, null),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new CorrectPredictionMetadataCommand(ticketId, confidenceScore: 0.95m),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(ticketId, CancellationToken.None);
        Assert.AreEqual(0.95m, aggregate.GetDetails().ConfidenceScore);
    }

    [TestMethod]
    public async Task MultipleMarks_ProducesCorrectState()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.92m, "精密予測"),
            CancellationToken.None);

        await _commandBus.PublishAsync(
            new AddPredictionMarkCommand(ticketId, "entry-1", "◎", 1, 95.0m, "本命"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new AddPredictionMarkCommand(ticketId, "entry-2", "○", 2, 80.0m, "対抗"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new AddPredictionMarkCommand(ticketId, "entry-3", "▲", 3, 65.0m, null),
            CancellationToken.None);

        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(
            ticketId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual(3, details.Marks.Count);
        Assert.AreEqual("精密予測", details.SummaryComment);
    }

    [TestMethod]
    public async Task FullLifecycle_ProducesCorrectState()
    {
        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, "race-abc", "AI", "model-v1", 0.92m, "精密予測"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new AddPredictionMarkCommand(ticketId, "entry-1", "◎", 1, 95.0m, "本命"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new AddBettingSuggestionCommand(ticketId, "WIN", "1", stakeAmount: 1000m),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new AddPredictionRationaleCommand(ticketId, "Horse", "horse-1", "SpeedFigure"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new FinalizePredictionTicketCommand(ticketId),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new EvaluatePredictionTicketCommand(ticketId, "race-abc", DateTimeOffset.UtcNow, 1, new[] { "WIN" }),
            CancellationToken.None);

        var aggregate = await _aggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(ticketId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual(TicketStatus.Finalized, details.TicketStatus);
        Assert.AreEqual(1, details.Marks.Count);
        Assert.AreEqual(1, details.BettingSuggestions.Count);
        Assert.AreEqual(1, details.Rationales.Count);
        Assert.AreEqual(1, details.Evaluations.Count);
    }
}

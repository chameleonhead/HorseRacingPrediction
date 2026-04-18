using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Commands;
using EventFlow.Extensions;
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
}

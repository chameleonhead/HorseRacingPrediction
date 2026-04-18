using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Commands;
using EventFlow.Extensions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Application.Tests;

[TestClass]
public class TrainerCommandTests
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
    public async Task RegisterTrainer_Succeeds()
    {
        var trainerId = TrainerId.New;
        var command = new RegisterTrainerCommand(trainerId, "池江泰寿", "いけえやすとし", affiliationCode: "栗東");

        var result = await _commandBus.PublishAsync(command, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<TrainerAggregate, TrainerId>(trainerId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual("池江泰寿", details.DisplayName);
        Assert.AreEqual("栗東", details.AffiliationCode);
    }

    [TestMethod]
    public async Task UpdateTrainerProfile_Succeeds()
    {
        var trainerId = TrainerId.New;
        await _commandBus.PublishAsync(
            new RegisterTrainerCommand(trainerId, "池江泰寿", "いけえやすとし"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new UpdateTrainerProfileCommand(trainerId, affiliationCode: "美浦"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<TrainerAggregate, TrainerId>(trainerId, CancellationToken.None);
        Assert.AreEqual("美浦", aggregate.GetDetails().AffiliationCode);
    }

    [TestMethod]
    public async Task MergeTrainerAlias_Succeeds()
    {
        var trainerId = TrainerId.New;
        await _commandBus.PublishAsync(
            new RegisterTrainerCommand(trainerId, "池江泰寿", "いけえやすとし"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new MergeTrainerAliasCommand(trainerId, "JRA_CODE", "T0001", "JRA", true),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<TrainerAggregate, TrainerId>(trainerId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().Aliases.Count);
    }

    [TestMethod]
    public async Task CorrectTrainerData_Succeeds()
    {
        var trainerId = TrainerId.New;
        await _commandBus.PublishAsync(
            new RegisterTrainerCommand(trainerId, "池江泰寿", "いけえやすとし"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new CorrectTrainerDataCommand(trainerId, displayName: "Ikee Yasutoshi", reason: "英語表記修正"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<TrainerAggregate, TrainerId>(trainerId, CancellationToken.None);
        Assert.AreEqual("Ikee Yasutoshi", aggregate.GetDetails().DisplayName);
    }
}

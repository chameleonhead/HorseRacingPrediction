using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Aggregates.ExecutionResults;
using EventFlow.Commands;
using EventFlow.Core;
using EventFlow.Queries;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Application.Queries.ReadModels;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public class DataCollectionWriteToolsTests
{
    private DataCollectionWriteTools _sut = null!;
    private FakeCommandBus _fakeBus = null!;
    private FakeQueryProcessor _fakeQueryProcessor = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeBus = new FakeCommandBus();
        _fakeQueryProcessor = new FakeQueryProcessor();
        _sut = new DataCollectionWriteTools(_fakeBus, _fakeQueryProcessor);
    }

    [TestMethod]
    public async Task UpsertHorse_NewHorse_PublishesRegisterCommand()
    {
        var horseId = await _sut.UpsertHorse("イクイノックス");

        StringAssert.StartsWith(horseId, "horse-");
        CollectionAssert.Contains(_fakeBus.PublishedCommandNames, "RegisterHorseCommand");
    }

    [TestMethod]
    public async Task UpsertHorse_ExistingHorse_PublishesUpdateCommand()
    {
        var existingModel = new HorseReadModel();
        existingModel.SetTestData("horse-11111111-1111-1111-1111-111111111111", "イクイノックス", "イクイノックス");
        _fakeQueryProcessor.HorseModelFactory = _ => existingModel;

        await _sut.UpsertHorse("イクイノックス");

        CollectionAssert.Contains(_fakeBus.PublishedCommandNames, "UpdateHorseProfileCommand");
    }

    [TestMethod]
    public async Task UpsertRaceEntry_ExistingRace_PublishesRegisterEntryCommand()
    {
        const string raceId = "race-22222222-2222-2222-2222-222222222222";
        var raceModel = new RacePredictionContextReadModel();
        raceModel.SetTestData(raceId, DateOnly.Parse("2024-10-27"), "TOKYO", 11, "天皇賞秋");
        _fakeQueryProcessor.RaceContextFactory = _ => raceModel;

        await _sut.UpsertRaceEntry(raceId, 1, "イクイノックス", "川田将雅", "木村哲也");

        CollectionAssert.Contains(_fakeBus.PublishedCommandNames, "RegisterEntryCommand");
        CollectionAssert.Contains(_fakeBus.PublishedCommandNames, "RegisterHorseCommand");
        CollectionAssert.Contains(_fakeBus.PublishedCommandNames, "RegisterJockeyCommand");
        CollectionAssert.Contains(_fakeBus.PublishedCommandNames, "RegisterTrainerCommand");
    }

    [TestMethod]
    public void DataCollectionWriteTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertRace"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertHorse"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertJockey"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertTrainer"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertRaceEntry"));
    }

    private sealed class FakeCommandBus : ICommandBus
    {
        public List<string> PublishedCommandNames { get; } = [];

        public Task<TExecutionResult> PublishAsync<TAggregate, TIdentity, TExecutionResult>(
            ICommand<TAggregate, TIdentity, TExecutionResult> command,
            CancellationToken cancellationToken)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
            where TExecutionResult : IExecutionResult
        {
            PublishedCommandNames.Add(command.GetType().Name);
            return Task.FromResult((TExecutionResult)(IExecutionResult)ExecutionResult.Success());
        }
    }

    private sealed class FakeQueryProcessor : IQueryProcessor
    {
        public Func<string, RacePredictionContextReadModel?>? RaceContextFactory { get; set; }
        public Func<string, HorseReadModel?>? HorseModelFactory { get; set; }
        public Func<string, JockeyReadModel?>? JockeyModelFactory { get; set; }
        public Func<string, TrainerReadModel?>? TrainerModelFactory { get; set; }

        public Task<TResult> ProcessAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
        {
            object? result = query switch
            {
                ReadModelByIdQuery<RacePredictionContextReadModel> raceQuery => RaceContextFactory?.Invoke(raceQuery.Id),
                ReadModelByIdQuery<HorseReadModel> horseQuery => HorseModelFactory?.Invoke(horseQuery.Id),
                ReadModelByIdQuery<JockeyReadModel> jockeyQuery => JockeyModelFactory?.Invoke(jockeyQuery.Id),
                ReadModelByIdQuery<TrainerReadModel> trainerQuery => TrainerModelFactory?.Invoke(trainerQuery.Id),
                _ => null
            };

            return Task.FromResult((TResult)result!);
        }
    }
}
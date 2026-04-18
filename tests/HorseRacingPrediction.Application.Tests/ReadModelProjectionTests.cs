using EventFlow;
using EventFlow.Commands;
using EventFlow.Extensions;
using EventFlow.Queries;
using EventFlow.ReadStores.InMemory;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Application.Tests;

[TestClass]
public class ReadModelProjectionTests
{
    private ServiceProvider _serviceProvider = null!;
    private ICommandBus _commandBus = null!;
    private IQueryProcessor _queryProcessor = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HorseWeightHistoryLocator>();
        services.AddSingleton<PredictionComparisonViewLocator>();
        services.AddSingleton<MemoBySubjectLocator>();
        services.AddEventFlow(options =>
        {
            options
                .AddDefaults(typeof(RaceAggregate).Assembly)
                .AddDefaults(typeof(CreateRaceCommand).Assembly)
                .UseInMemoryReadStoreFor<HorseReadModel>()
                .UseInMemoryReadStoreFor<JockeyReadModel>()
                .UseInMemoryReadStoreFor<TrainerReadModel>()
                .UseInMemoryReadStoreFor<RacePredictionContextReadModel>()
                .UseInMemoryReadStoreFor<RaceResultViewReadModel>()
                .UseInMemoryReadStoreFor<PredictionTicketReadModel>()
                .UseInMemoryReadStoreFor<HorseWeightHistoryReadModel, HorseWeightHistoryLocator>()
                .UseInMemoryReadStoreFor<PredictionComparisonViewReadModel, PredictionComparisonViewLocator>()
                .UseInMemoryReadStoreFor<MemoBySubjectReadModel, MemoBySubjectLocator>();
        });
        _serviceProvider = services.BuildServiceProvider();
        _commandBus = _serviceProvider.GetRequiredService<ICommandBus>();
        _queryProcessor = _serviceProvider.GetRequiredService<IQueryProcessor>();
    }

    [TestCleanup]
    public void Cleanup() => _serviceProvider.Dispose();

    [TestMethod]
    public async Task HorseReadModel_AfterRegisterAndUpdate_ReflectsCurrentState()
    {
        var horseId = HorseId.New;
        await _commandBus.PublishAsync(
            new RegisterHorseCommand(horseId, "ディープインパクト", "ディープインパクト", sexCode: "M",
                birthDate: new DateOnly(2002, 3, 25)),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new UpdateHorseProfileCommand(horseId, registeredName: "ディープインパクト（更新）"),
            CancellationToken.None);

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<HorseReadModel>(horseId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(horseId.Value, readModel.HorseId);
        Assert.AreEqual("M", readModel.SexCode);
        Assert.AreEqual(new DateOnly(2002, 3, 25), readModel.BirthDate);
    }

    [TestMethod]
    public async Task JockeyReadModel_AfterRegister_ReflectsCorrectData()
    {
        var jockeyId = JockeyId.New;
        await _commandBus.PublishAsync(
            new RegisterJockeyCommand(jockeyId, "武豊", "武豊", affiliationCode: "JRA"),
            CancellationToken.None);

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<JockeyReadModel>(jockeyId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(jockeyId.Value, readModel.JockeyId);
        Assert.AreEqual("武豊", readModel.DisplayName);
        Assert.AreEqual("JRA", readModel.AffiliationCode);
    }

    [TestMethod]
    public async Task TrainerReadModel_AfterRegister_ReflectsCorrectData()
    {
        var trainerId = TrainerId.New;
        await _commandBus.PublishAsync(
            new RegisterTrainerCommand(trainerId, "池江泰寿", "池江泰寿", affiliationCode: "JRA"),
            CancellationToken.None);

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<TrainerReadModel>(trainerId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(trainerId.Value, readModel.TrainerId);
        Assert.AreEqual("池江泰寿", readModel.DisplayName);
        Assert.AreEqual("JRA", readModel.AffiliationCode);
    }

    [TestMethod]
    public async Task RacePredictionContextReadModel_AfterCreateAndPublishCard_ReflectsStatus()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 1), "TOKYO", 11, "東京優駿"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new PublishRaceCardCommand(raceId, 18),
            CancellationToken.None);

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(raceId.Value, readModel.RaceId);
        Assert.AreEqual("東京優駿", readModel.RaceName);
        Assert.AreEqual(RaceStatus.CardPublished, readModel.Status);
    }

    [TestMethod]
    public async Task RaceResultViewReadModel_AfterFullLifecycle_ReflectsResult()
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

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RaceResultViewReadModel>(raceId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual("有馬記念", readModel.RaceName);
        Assert.AreEqual(RaceStatus.ResultDeclared, readModel.Status);
        Assert.AreEqual(16, readModel.EntryCount);
        Assert.AreEqual("イクイノックス", readModel.WinningHorseName);
        Assert.IsNotNull(readModel.ResultDeclaredAt);
    }

    [TestMethod]
    public async Task PredictionTicketReadModel_AfterCreateAndAddMark_ReflectsMarks()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 1), "TOKYO", 11, "東京優駿"),
            CancellationToken.None);

        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, raceId.Value, "AI", "model-v1", 0.8m, null),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new AddPredictionMarkCommand(ticketId, "entry-1", "◎", 1, 95m, null),
            CancellationToken.None);

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<PredictionTicketReadModel>(ticketId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(raceId.Value, readModel.RaceId);
        Assert.AreEqual("AI", readModel.PredictorType);
        Assert.AreEqual(1, readModel.Marks.Count);
        Assert.AreEqual("entry-1", readModel.Marks[0].EntryId);
        Assert.AreEqual("◎", readModel.Marks[0].MarkCode);
    }

    [TestMethod]
    public async Task HorseWeightHistoryReadModel_AfterEntryRegistered_TracksDeclaredWeight()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 1), "TOKYO", 11, "東京優駿"),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new PublishRaceCardCommand(raceId, 18),
            CancellationToken.None);

        var horseId = HorseId.New;
        await _commandBus.PublishAsync(
            new RegisterEntryCommand(raceId, "entry-1", horseId.Value, 1,
                jockeyId: null, trainerId: null, gateNumber: 1, assignedWeight: 57.0m,
                sexCode: "M", age: 3, declaredWeight: 480m, declaredWeightDiff: -2m),
            CancellationToken.None);

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<HorseWeightHistoryReadModel>(horseId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(horseId.Value, readModel.HorseId);
        Assert.AreEqual(1, readModel.WeightHistory.Count);
        Assert.AreEqual(raceId.Value, readModel.WeightHistory[0].RaceId);
        Assert.AreEqual(480m, readModel.WeightHistory[0].DeclaredWeight);
        Assert.AreEqual(-2m, readModel.WeightHistory[0].DeclaredWeightDiff);
    }

    [TestMethod]
    public async Task HorseWeightHistoryReadModel_AcrossMultipleRaces_AccumulatesHistory()
    {
        var horseId = HorseId.New;

        var raceId1 = RaceId.New;
        var raceId2 = RaceId.New;

        foreach (var (rid, weight, diff) in new[] { (raceId1, 480m, 0m), (raceId2, 476m, -4m) })
        {
            await _commandBus.PublishAsync(
                new CreateRaceCommand(rid, new DateOnly(2025, 5, 1), "TOKYO", 1, "テスト"),
                CancellationToken.None);
            await _commandBus.PublishAsync(new PublishRaceCardCommand(rid, 10), CancellationToken.None);
            await _commandBus.PublishAsync(
                new RegisterEntryCommand(rid, $"entry-{rid.Value}", horseId.Value, 1,
                    null, null, null, null, "M", 3, weight, diff),
                CancellationToken.None);
        }

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<HorseWeightHistoryReadModel>(horseId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(2, readModel.WeightHistory.Count);
    }

    [TestMethod]
    public async Task PredictionComparisonViewReadModel_AfterTicketAndResult_ContainsBoth()
    {
        var raceId = RaceId.New;
        await _commandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2025, 6, 1), "TOKYO", 11, "東京優駿"),
            CancellationToken.None);

        var ticketId = PredictionTicketId.New;
        await _commandBus.PublishAsync(
            new CreatePredictionTicketCommand(ticketId, raceId.Value, "AI", "model-v1", 0.9m, null),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new AddPredictionMarkCommand(ticketId, "entry-1", "◎", 1, 90m, null),
            CancellationToken.None);

        await _commandBus.PublishAsync(
            new PublishRaceCardCommand(raceId, 18),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new DeclareRaceResultCommand(raceId, "ドウデュース", DateTimeOffset.UtcNow),
            CancellationToken.None);

        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<PredictionComparisonViewReadModel>(raceId.Value), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(raceId.Value, readModel.RaceId);
        Assert.AreEqual("東京優駿", readModel.RaceName);
        Assert.AreEqual("ドウデュース", readModel.WinningHorseName);
        Assert.AreEqual(1, readModel.PredictionTickets.Count);
        Assert.AreEqual(1, readModel.PredictionTickets[0].Marks.Count);
    }

    [TestMethod]
    public async Task MemoBySubjectReadModel_AfterCreate_AppearsForEachSubject()
    {
        var memoId = MemoId.New;
        var horseId = HorseId.New.Value;
        var trainerId = TrainerId.New.Value;
        var subjects = new[]
        {
            new MemoSubject(MemoSubjectType.Horse, horseId),
            new MemoSubject(MemoSubjectType.Trainer, trainerId)
        };

        await _commandBus.PublishAsync(
            new CreateMemoCommand(memoId, "author-1", "Observation", "調教師×馬のメモ",
                DateTimeOffset.UtcNow, subjects),
            CancellationToken.None);

        var horseKey = MemoBySubjectLocator.MakeKey(MemoSubjectType.Horse, horseId);
        var horseReadModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<MemoBySubjectReadModel>(horseKey), CancellationToken.None);

        Assert.IsNotNull(horseReadModel);
        Assert.AreEqual(1, horseReadModel.Memos.Count);
        Assert.AreEqual(memoId.Value, horseReadModel.Memos[0].MemoId);
        Assert.AreEqual("Observation", horseReadModel.Memos[0].MemoType);

        var trainerKey = MemoBySubjectLocator.MakeKey(MemoSubjectType.Trainer, trainerId);
        var trainerReadModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<MemoBySubjectReadModel>(trainerKey), CancellationToken.None);

        Assert.IsNotNull(trainerReadModel);
        Assert.AreEqual(1, trainerReadModel.Memos.Count);
        Assert.AreEqual(memoId.Value, trainerReadModel.Memos[0].MemoId);
    }

    [TestMethod]
    public async Task MemoBySubjectReadModel_AfterDelete_MemoIsRemoved()
    {
        var memoId = MemoId.New;
        var horseId = HorseId.New.Value;
        var subjects = new[] { new MemoSubject(MemoSubjectType.Horse, horseId) };

        await _commandBus.PublishAsync(
            new CreateMemoCommand(memoId, null, "Note", "内容", DateTimeOffset.UtcNow, subjects),
            CancellationToken.None);
        await _commandBus.PublishAsync(
            new DeleteMemoCommand(memoId),
            CancellationToken.None);

        var key = MemoBySubjectLocator.MakeKey(MemoSubjectType.Horse, horseId);
        var readModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<MemoBySubjectReadModel>(key), CancellationToken.None);

        Assert.IsNotNull(readModel);
        Assert.AreEqual(0, readModel.Memos.Count);
    }

    [TestMethod]
    public async Task MemoBySubjectReadModel_AfterChangeSubjects_ReflectsNewSubjects()
    {
        var memoId = MemoId.New;
        var horseId = HorseId.New.Value;
        var jockeyId = JockeyId.New.Value;

        await _commandBus.PublishAsync(
            new CreateMemoCommand(memoId, null, "Note", "馬のメモ", DateTimeOffset.UtcNow,
                new[] { new MemoSubject(MemoSubjectType.Horse, horseId) }),
            CancellationToken.None);

        await _commandBus.PublishAsync(
            new ChangeMemoSubjectsCommand(memoId, new[]
            {
                new MemoSubject(MemoSubjectType.Horse, horseId),
                new MemoSubject(MemoSubjectType.Jockey, jockeyId)
            }),
            CancellationToken.None);

        var jockeyKey = MemoBySubjectLocator.MakeKey(MemoSubjectType.Jockey, jockeyId);
        var jockeyReadModel = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<MemoBySubjectReadModel>(jockeyKey), CancellationToken.None);

        Assert.IsNotNull(jockeyReadModel);
        Assert.AreEqual(1, jockeyReadModel.Memos.Count);
        Assert.AreEqual(memoId.Value, jockeyReadModel.Memos[0].MemoId);
    }
}

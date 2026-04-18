using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Commands;
using EventFlow.Extensions;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Races;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Application.Tests;

[TestClass]
public class HorseCommandTests
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
            options.AddDefaults(typeof(RegisterHorseCommand).Assembly);
        });
        _serviceProvider = services.BuildServiceProvider();
        _commandBus = _serviceProvider.GetRequiredService<ICommandBus>();
        _aggregateStore = _serviceProvider.GetRequiredService<IAggregateStore>();
    }

    [TestCleanup]
    public void Cleanup() => _serviceProvider.Dispose();

    [TestMethod]
    public async Task RegisterHorse_Succeeds()
    {
        var horseId = HorseId.New;
        var command = new RegisterHorseCommand(horseId, "ディープインパクト", "ディープインパクト",
            sexCode: "M", birthDate: new DateOnly(2002, 3, 25));

        var result = await _commandBus.PublishAsync(command, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<HorseAggregate, HorseId>(horseId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual("ディープインパクト", details.RegisteredName);
        Assert.AreEqual("M", details.SexCode);
    }

    [TestMethod]
    public async Task UpdateHorseProfile_Succeeds()
    {
        var horseId = HorseId.New;
        await _commandBus.PublishAsync(
            new RegisterHorseCommand(horseId, "ディープインパクト", "ディープインパクト"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new UpdateHorseProfileCommand(horseId, sexCode: "G"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<HorseAggregate, HorseId>(horseId, CancellationToken.None);
        Assert.AreEqual("G", aggregate.GetDetails().SexCode);
    }

    [TestMethod]
    public async Task MergeHorseAlias_Succeeds()
    {
        var horseId = HorseId.New;
        await _commandBus.PublishAsync(
            new RegisterHorseCommand(horseId, "ディープインパクト", "ディープインパクト"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new MergeHorseAliasCommand(horseId, "JRA_CODE", "2002100816", "JRA", true),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<HorseAggregate, HorseId>(horseId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().Aliases.Count);
    }

    [TestMethod]
    public async Task CorrectHorseData_Succeeds()
    {
        var horseId = HorseId.New;
        await _commandBus.PublishAsync(
            new RegisterHorseCommand(horseId, "ディープインパクト", "ディープインパクト"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new CorrectHorseDataCommand(horseId, registeredName: "Deep Impact", reason: "英語名修正"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<HorseAggregate, HorseId>(horseId, CancellationToken.None);
        Assert.AreEqual("Deep Impact", aggregate.GetDetails().RegisteredName);
    }
}

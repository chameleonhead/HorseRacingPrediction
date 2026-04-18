using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Commands;
using EventFlow.Extensions;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Races;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Application.Tests;

[TestClass]
public class JockeyCommandTests
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
            options.AddDefaults(typeof(RegisterJockeyCommand).Assembly);
        });
        _serviceProvider = services.BuildServiceProvider();
        _commandBus = _serviceProvider.GetRequiredService<ICommandBus>();
        _aggregateStore = _serviceProvider.GetRequiredService<IAggregateStore>();
    }

    [TestCleanup]
    public void Cleanup() => _serviceProvider.Dispose();

    [TestMethod]
    public async Task RegisterJockey_Succeeds()
    {
        var jockeyId = JockeyId.New;
        var command = new RegisterJockeyCommand(jockeyId, "武豊", "たけゆたか", affiliationCode: "JRA");

        var result = await _commandBus.PublishAsync(command, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<JockeyAggregate, JockeyId>(jockeyId, CancellationToken.None);
        var details = aggregate.GetDetails();
        Assert.AreEqual("武豊", details.DisplayName);
        Assert.AreEqual("JRA", details.AffiliationCode);
    }

    [TestMethod]
    public async Task UpdateJockeyProfile_Succeeds()
    {
        var jockeyId = JockeyId.New;
        await _commandBus.PublishAsync(
            new RegisterJockeyCommand(jockeyId, "武豊", "たけゆたか"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new UpdateJockeyProfileCommand(jockeyId, affiliationCode: "FREE"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<JockeyAggregate, JockeyId>(jockeyId, CancellationToken.None);
        Assert.AreEqual("FREE", aggregate.GetDetails().AffiliationCode);
    }

    [TestMethod]
    public async Task MergeJockeyAlias_Succeeds()
    {
        var jockeyId = JockeyId.New;
        await _commandBus.PublishAsync(
            new RegisterJockeyCommand(jockeyId, "武豊", "たけゆたか"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new MergeJockeyAliasCommand(jockeyId, "JRA_CODE", "00001", "JRA", true),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<JockeyAggregate, JockeyId>(jockeyId, CancellationToken.None);
        Assert.AreEqual(1, aggregate.GetDetails().Aliases.Count);
    }

    [TestMethod]
    public async Task CorrectJockeyData_Succeeds()
    {
        var jockeyId = JockeyId.New;
        await _commandBus.PublishAsync(
            new RegisterJockeyCommand(jockeyId, "武豊", "たけゆたか"),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new CorrectJockeyDataCommand(jockeyId, displayName: "Take Yutaka", reason: "英語表記修正"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        var aggregate = await _aggregateStore.LoadAsync<JockeyAggregate, JockeyId>(jockeyId, CancellationToken.None);
        Assert.AreEqual("Take Yutaka", aggregate.GetDetails().DisplayName);
    }
}

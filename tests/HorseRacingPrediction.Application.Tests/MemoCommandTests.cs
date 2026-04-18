using EventFlow;
using EventFlow.Commands;
using EventFlow.Extensions;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Races;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Application.Tests;

[TestClass]
public class MemoCommandTests
{
    private ServiceProvider _serviceProvider = null!;
    private ICommandBus _commandBus = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventFlow(options =>
        {
            options.AddDefaults(typeof(RaceAggregate).Assembly);
            options.AddDefaults(typeof(CreateMemoCommand).Assembly);
        });
        _serviceProvider = services.BuildServiceProvider();
        _commandBus = _serviceProvider.GetRequiredService<ICommandBus>();
    }

    [TestCleanup]
    public void Cleanup() => _serviceProvider.Dispose();

    [TestMethod]
    public async Task CreateMemo_WithSingleSubject_Succeeds()
    {
        var memoId = MemoId.New;
        var subjects = new[] { new MemoSubject(MemoSubjectType.Horse, "horse-1") };
        var command = new CreateMemoCommand(memoId, "author-1", "TrainingNote",
            "好調です", DateTimeOffset.UtcNow, subjects);

        var result = await _commandBus.PublishAsync(command, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task CreateMemo_WithMultipleSubjects_Succeeds()
    {
        var memoId = MemoId.New;
        var subjects = new[]
        {
            new MemoSubject(MemoSubjectType.Horse, "horse-1"),
            new MemoSubject(MemoSubjectType.Trainer, "trainer-1")
        };
        var command = new CreateMemoCommand(memoId, null, "Observation",
            "調教師×馬のメモ", DateTimeOffset.UtcNow, subjects);

        var result = await _commandBus.PublishAsync(command, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task UpdateMemo_AfterCreate_Succeeds()
    {
        var memoId = MemoId.New;
        await _commandBus.PublishAsync(
            new CreateMemoCommand(memoId, null, "Note", "初期内容", DateTimeOffset.UtcNow,
                new[] { new MemoSubject(MemoSubjectType.Horse, "horse-1") }),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new UpdateMemoCommand(memoId, content: "更新された内容"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task DeleteMemo_AfterCreate_Succeeds()
    {
        var memoId = MemoId.New;
        await _commandBus.PublishAsync(
            new CreateMemoCommand(memoId, null, "Note", "内容", DateTimeOffset.UtcNow,
                new[] { new MemoSubject(MemoSubjectType.Jockey, "jockey-1") }),
            CancellationToken.None);

        var result = await _commandBus.PublishAsync(
            new DeleteMemoCommand(memoId),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task ChangeMemoSubjects_Succeeds()
    {
        var memoId = MemoId.New;
        await _commandBus.PublishAsync(
            new CreateMemoCommand(memoId, null, "Note", "内容", DateTimeOffset.UtcNow,
                new[] { new MemoSubject(MemoSubjectType.Horse, "horse-1") }),
            CancellationToken.None);

        var newSubjects = new[]
        {
            new MemoSubject(MemoSubjectType.Horse, "horse-1"),
            new MemoSubject(MemoSubjectType.Trainer, "trainer-1")
        };
        var result = await _commandBus.PublishAsync(
            new ChangeMemoSubjectsCommand(memoId, newSubjects),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }
}

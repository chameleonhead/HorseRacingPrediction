using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Aggregates.ExecutionResults;
using EventFlow.Commands;
using EventFlow.Core;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.SemanticKernel;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// PredictionWriteTools のモック ICommandBus を使ったユニットテスト。
/// </summary>
[TestClass]
public class PredictionWriteToolsTests
{
    private PredictionWriteTools _sut = null!;
    private FakeCommandBus _fakeBus = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeBus = new FakeCommandBus();
        _sut = new PredictionWriteTools(_fakeBus);
    }

    // ------------------------------------------------------------------ //
    // CreatePredictionTicket
    // ------------------------------------------------------------------ //

    private static string ValidTicketId => $"predictionticket-{Guid.NewGuid()}";

    [TestMethod]
    public async Task CreatePredictionTicket_Success_ReturnsTicketId()
    {
        _fakeBus.IsSuccess = true;

        var result = await _sut.CreatePredictionTicket(
            raceId: "race-001",
            predictorType: "AI",
            predictorId: "PredictionAgent",
            confidenceScore: 0.8m,
            summaryComment: "テスト予測");

        Assert.IsFalse(string.IsNullOrEmpty(result), "予測票IDが返されること");
        StringAssert.Contains(result, "predictionticket-", "EventFlow Identity 形式であること");
    }

    [TestMethod]
    public async Task CreatePredictionTicket_Failure_ThrowsInvalidOperationException()
    {
        _fakeBus.IsSuccess = false;

        try
        {
            await _sut.CreatePredictionTicket("race-001", "AI", "Agent", 0.5m);
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    // ------------------------------------------------------------------ //
    // AddPredictionMark
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task AddPredictionMark_Success_ReturnsConfirmationMessage()
    {
        _fakeBus.IsSuccess = true;
        var ticketId = ValidTicketId;

        var result = await _sut.AddPredictionMark(
            predictionTicketId: ticketId,
            entryId: "entry-001",
            markCode: "◎",
            predictedRank: 1,
            score: 0.9m,
            comment: "本命");

        StringAssert.Contains(result, "entry-001", "エントリーIDが含まれること");
        StringAssert.Contains(result, "◎", "印コードが含まれること");
    }

    [TestMethod]
    public async Task AddPredictionMark_Failure_ThrowsInvalidOperationException()
    {
        _fakeBus.IsSuccess = false;
        var ticketId = ValidTicketId;

        try
        {
            await _sut.AddPredictionMark(ticketId, "entry-001", "◎", 1, 0.9m);
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    // ------------------------------------------------------------------ //
    // AddPredictionRationale
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task AddPredictionRationale_Success_ReturnsConfirmationMessage()
    {
        _fakeBus.IsSuccess = true;
        var ticketId = ValidTicketId;

        var result = await _sut.AddPredictionRationale(
            predictionTicketId: ticketId,
            subjectType: "Horse",
            subjectId: "horse-001",
            signalType: "RecentForm",
            signalValue: "3連勝中",
            explanationText: "直近3走すべて1着");

        StringAssert.Contains(result, "Horse", "対象種別が含まれること");
        StringAssert.Contains(result, "horse-001", "対象IDが含まれること");
        StringAssert.Contains(result, "RecentForm", "シグナル種別が含まれること");
    }

    // ------------------------------------------------------------------ //
    // FinalizePredictionTicket
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FinalizePredictionTicket_Success_ReturnsConfirmationMessage()
    {
        _fakeBus.IsSuccess = true;
        var ticketId = ValidTicketId;

        var result = await _sut.FinalizePredictionTicket(ticketId);

        StringAssert.Contains(result, ticketId, "予測票IDが含まれること");
        StringAssert.Contains(result, "確定", "確定メッセージが含まれること");
    }

    [TestMethod]
    public async Task FinalizePredictionTicket_Failure_ThrowsInvalidOperationException()
    {
        _fakeBus.IsSuccess = false;
        var ticketId = ValidTicketId;

        try
        {
            await _sut.FinalizePredictionTicket(ticketId);
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    // ------------------------------------------------------------------ //
    // KernelFunction registration
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void PredictionWriteTools_RegisteredAsKernelPlugin_HasExpectedFunctions()
    {
        var kernel = Kernel.CreateBuilder().Build();
        kernel.Plugins.AddFromObject(_sut, pluginName: "PredictionWrite");

        var plugin = kernel.Plugins["PredictionWrite"];
        Assert.IsTrue(plugin.Contains("CreatePredictionTicket"), "CreatePredictionTicket が登録されていること");
        Assert.IsTrue(plugin.Contains("AddPredictionMark"), "AddPredictionMark が登録されていること");
        Assert.IsTrue(plugin.Contains("AddPredictionRationale"), "AddPredictionRationale が登録されていること");
        Assert.IsTrue(plugin.Contains("FinalizePredictionTicket"), "FinalizePredictionTicket が登録されていること");
    }

    // ------------------------------------------------------------------ //
    // Fake ICommandBus
    // ------------------------------------------------------------------ //

    private sealed class FakeCommandBus : ICommandBus
    {
        public bool IsSuccess { get; set; } = true;

        public Task<TExecutionResult> PublishAsync<TAggregate, TIdentity, TExecutionResult>(
            ICommand<TAggregate, TIdentity, TExecutionResult> command,
            CancellationToken cancellationToken)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
            where TExecutionResult : IExecutionResult
        {
            var result = IsSuccess
                ? ExecutionResult.Success()
                : ExecutionResult.Failed();
            return Task.FromResult((TExecutionResult)(IExecutionResult)result);
        }
    }
}

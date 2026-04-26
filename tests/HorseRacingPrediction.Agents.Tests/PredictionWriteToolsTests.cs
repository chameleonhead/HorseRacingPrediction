using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// PredictionWriteTools の FakePredictionWriteService を使ったユニットテスト。
/// </summary>
[TestClass]
public class PredictionWriteToolsTests
{
    private PredictionWriteTools _sut = null!;
    private FakePredictionWriteService _fakeService = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeService = new FakePredictionWriteService();
        _sut = new PredictionWriteTools(_fakeService);
    }

    // ------------------------------------------------------------------ //
    // CreatePredictionTicket
    // ------------------------------------------------------------------ //

    private static string ValidTicketId => $"predictionticket-{Guid.NewGuid()}";

    [TestMethod]
    public async Task CreatePredictionTicket_Success_ReturnsTicketId()
    {
        _fakeService.IsSuccess = true;

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
        _fakeService.IsSuccess = false;

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
        _fakeService.IsSuccess = true;
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
        _fakeService.IsSuccess = false;
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
        _fakeService.IsSuccess = true;
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
        _fakeService.IsSuccess = true;
        var ticketId = ValidTicketId;

        var result = await _sut.FinalizePredictionTicket(ticketId);

        StringAssert.Contains(result, ticketId, "予測票IDが含まれること");
        StringAssert.Contains(result, "確定", "確定メッセージが含まれること");
    }

    [TestMethod]
    public async Task FinalizePredictionTicket_Failure_ThrowsInvalidOperationException()
    {
        _fakeService.IsSuccess = false;
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
    // GetAITools registration
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void PredictionWriteTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.IsTrue(tools.Any(t => t.Name == "CreatePredictionTicket"), "CreatePredictionTicket が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "AddPredictionMark"), "AddPredictionMark が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "AddPredictionRationale"), "AddPredictionRationale が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FinalizePredictionTicket"), "FinalizePredictionTicket が登録されていること");
    }

    // ------------------------------------------------------------------ //
    // Fake IPredictionWriteService
    // ------------------------------------------------------------------ //

    private sealed class FakePredictionWriteService : IPredictionWriteService
    {
        public bool IsSuccess { get; set; } = true;

        public Task<string> CreatePredictionTicketAsync(
            string raceId, string predictorType, string predictorId,
            decimal confidenceScore, string? summaryComment,
            CancellationToken cancellationToken = default)
        {
            if (!IsSuccess)
                throw new InvalidOperationException($"予測票の作成に失敗しました: raceId={raceId}");
            return Task.FromResult($"predictionticket-{Guid.NewGuid()}");
        }

        public Task AddPredictionMarkAsync(
            string predictionTicketId, string entryId, string markCode,
            int predictedRank, decimal score, string? comment,
            CancellationToken cancellationToken = default)
        {
            if (!IsSuccess)
                throw new InvalidOperationException($"予測印の追加に失敗しました: ticketId={predictionTicketId}");
            return Task.CompletedTask;
        }

        public Task AddPredictionRationaleAsync(
            string predictionTicketId, string subjectType, string subjectId,
            string signalType, string? signalValue, string? explanationText,
            CancellationToken cancellationToken = default)
        {
            if (!IsSuccess)
                throw new InvalidOperationException($"予測根拠の追加に失敗しました: ticketId={predictionTicketId}");
            return Task.CompletedTask;
        }

        public Task FinalizePredictionTicketAsync(
            string predictionTicketId,
            CancellationToken cancellationToken = default)
        {
            if (!IsSuccess)
                throw new InvalidOperationException($"予測票の確定に失敗しました: ticketId={predictionTicketId}");
            return Task.CompletedTask;
        }
    }
}

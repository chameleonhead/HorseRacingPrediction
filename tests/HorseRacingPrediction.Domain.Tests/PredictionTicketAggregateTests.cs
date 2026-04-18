using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public class PredictionTicketAggregateTests
{
    [TestMethod]
    public void Create_SetsPredictionDetailsCorrectly()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);

        sut.Create("race-abc", "AI", "model-v1", 0.85m, "高確率予想");

        var details = sut.GetDetails();
        Assert.AreEqual("race-abc", details.RaceId);
        Assert.AreEqual("AI", details.PredictorType);
        Assert.AreEqual("model-v1", details.PredictorId);
        Assert.AreEqual(0.85m, details.ConfidenceScore);
        Assert.AreEqual("高確率予想", details.SummaryComment);
        Assert.IsNotNull(details.PredictedAt);
    }

    [TestMethod]
    public void Create_WhenAlreadyCreated_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.Create("race-def", "Human", "user-1", 0.5m, null));
    }

    [TestMethod]
    public void AddMark_AppendsMark()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);

        sut.AddMark("entry-1", "◎", 1, 90.5m, "本命");
        sut.AddMark("entry-2", "○", 2, 75.0m, null);

        var details = sut.GetDetails();
        Assert.AreEqual(2, details.Marks.Count);
        var marks = details.Marks.ToList();

        Assert.AreEqual("entry-1", marks[0].EntryId);
        Assert.AreEqual("◎", marks[0].MarkCode);
        Assert.AreEqual(1, marks[0].PredictedRank);
        Assert.AreEqual(90.5m, marks[0].Score);
        Assert.AreEqual("本命", marks[0].Comment);

        Assert.AreEqual("entry-2", marks[1].EntryId);
        Assert.AreEqual("○", marks[1].MarkCode);
        Assert.AreEqual(2, marks[1].PredictedRank);
        Assert.AreEqual(75.0m, marks[1].Score);
        Assert.IsNull(marks[1].Comment);
    }

    [TestMethod]
    public void AddMark_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.AddMark("entry-1", "◎", 1, 90.5m, null));
    }

    [TestMethod]
    public void GetDetails_ReturnsCorrectAggregateId()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        Assert.AreEqual(sut.Id.Value, sut.GetDetails().PredictionTicketId);
    }

    [TestMethod]
    public void GetDetails_EmptyMarksWhenOnlyCreated()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);

        Assert.AreEqual(0, sut.GetDetails().Marks.Count);
    }

    [TestMethod]
    public void AddBettingSuggestion_AppendsSuggestion()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);

        sut.AddBettingSuggestion("WIN", "1", stakeAmount: 1000m, expectedValue: 1.5m);

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.BettingSuggestions.Count);
        var suggestion = details.BettingSuggestions.First();
        Assert.AreEqual("WIN", suggestion.BetTypeCode);
        Assert.AreEqual("1", suggestion.SelectionExpression);
        Assert.AreEqual(1000m, suggestion.StakeAmount);
        Assert.AreEqual(1.5m, suggestion.ExpectedValue);
    }

    [TestMethod]
    public void AddBettingSuggestion_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.AddBettingSuggestion("WIN", "1"));
    }

    [TestMethod]
    public void AddBettingSuggestion_WhenFinalized_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);
        sut.FinalizeTicket();

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.AddBettingSuggestion("WIN", "1"));
    }

    [TestMethod]
    public void AddRationale_AppendsRationale()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);

        sut.AddRationale("Horse", "horse-1", "SpeedFigure", "95", "高いスピード指数");

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.Rationales.Count);
        var rationale = details.Rationales.First();
        Assert.AreEqual("Horse", rationale.SubjectType);
        Assert.AreEqual("horse-1", rationale.SubjectId);
        Assert.AreEqual("SpeedFigure", rationale.SignalType);
        Assert.AreEqual("95", rationale.SignalValue);
        Assert.AreEqual("高いスピード指数", rationale.ExplanationText);
    }

    [TestMethod]
    public void AddRationale_WhenWithdrawn_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);
        sut.Withdraw("取消");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.AddRationale("Horse", "horse-1", "SpeedFigure"));
    }

    [TestMethod]
    public void FinalizeTicket_FromDraft_SetsStatusFinalized()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);

        sut.FinalizeTicket();

        Assert.AreEqual(TicketStatus.Finalized, sut.GetDetails().TicketStatus);
    }

    [TestMethod]
    public void FinalizeTicket_WhenNotDraft_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);
        sut.FinalizeTicket();

        Assert.ThrowsException<InvalidOperationException>(() => sut.FinalizeTicket());
    }

    [TestMethod]
    public void Withdraw_SetsStatusWithdrawn()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);

        sut.Withdraw("予想変更");

        Assert.AreEqual(TicketStatus.Withdrawn, sut.GetDetails().TicketStatus);
    }

    [TestMethod]
    public void Withdraw_WhenAlreadyWithdrawn_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);
        sut.Withdraw();

        Assert.ThrowsException<InvalidOperationException>(() => sut.Withdraw());
    }

    [TestMethod]
    public void AddMark_WhenFinalized_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);
        sut.FinalizeTicket();

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.AddMark("entry-1", "◎", 1, 90.5m, null));
    }

    [TestMethod]
    public void AddMark_WhenWithdrawn_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);
        sut.Withdraw();

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.AddMark("entry-1", "◎", 1, 90.5m, null));
    }

    [TestMethod]
    public void Evaluate_AddsEvaluation()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);
        sut.FinalizeTicket();
        var evaluatedAt = DateTimeOffset.UtcNow;

        sut.Evaluate("race-abc", evaluatedAt, 1,
            new[] { "WIN", "PLACE" }, scoreSummary: 85.0m, returnAmount: 1500m, roi: 1.5m);

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.Evaluations.Count);
        var eval = details.Evaluations.First();
        Assert.AreEqual("race-abc", eval.RaceId);
        Assert.AreEqual(1, eval.EvaluationRevision);
        Assert.AreEqual(2, eval.HitTypeCodes.Count);
        Assert.AreEqual(85.0m, eval.ScoreSummary);
        Assert.AreEqual(1500m, eval.ReturnAmount);
        Assert.AreEqual(1.5m, eval.Roi);
    }

    [TestMethod]
    public void RecalculateEvaluation_AppendsNewEvaluation()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, null);
        sut.FinalizeTicket();
        var evaluatedAt = DateTimeOffset.UtcNow;
        sut.Evaluate("race-abc", evaluatedAt, 1, new[] { "WIN" });

        sut.RecalculateEvaluation("race-abc", evaluatedAt, 2,
            new[] { "WIN", "PLACE" }, scoreSummary: 90.0m);

        Assert.AreEqual(2, sut.GetDetails().Evaluations.Count);
    }

    [TestMethod]
    public void CorrectMetadata_UpdatesSpecifiedFields()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);
        sut.Create("race-abc", "AI", "model-v1", 0.85m, "初期コメント");

        sut.CorrectMetadata(confidenceScore: 0.95m, summaryComment: "修正コメント", reason: "スコア修正");

        var details = sut.GetDetails();
        Assert.AreEqual(0.95m, details.ConfidenceScore);
        Assert.AreEqual("修正コメント", details.SummaryComment);
    }

    [TestMethod]
    public void CorrectMetadata_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.CorrectMetadata(confidenceScore: 0.5m));
    }

    [TestMethod]
    public void FullLifecycle_ProducesCorrectState()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);

        sut.Create("race-abc", "AI", "model-v1", 0.92m, "精密予測");
        sut.AddMark("entry-1", "◎", 1, 95.0m, "本命");
        sut.AddMark("entry-2", "○", 2, 80.0m, "対抗");
        sut.AddMark("entry-3", "▲", 3, 65.0m, null);
        sut.AddBettingSuggestion("TRIFECTA", "1-2-3", stakeAmount: 500m);
        sut.AddRationale("Horse", "horse-1", "SpeedFigure", "95", "高速");
        sut.FinalizeTicket();
        sut.Evaluate("race-abc", DateTimeOffset.UtcNow, 1,
            new[] { "WIN" }, scoreSummary: 100m, returnAmount: 5000m, roi: 10.0m);

        var details = sut.GetDetails();
        Assert.AreEqual("race-abc", details.RaceId);
        Assert.AreEqual(0.92m, details.ConfidenceScore);
        Assert.AreEqual("精密予測", details.SummaryComment);
        Assert.AreEqual(TicketStatus.Finalized, details.TicketStatus);
        Assert.AreEqual(3, details.Marks.Count);
        Assert.AreEqual(1, details.BettingSuggestions.Count);
        Assert.AreEqual(1, details.Rationales.Count);
        Assert.AreEqual(1, details.Evaluations.Count);
    }
}

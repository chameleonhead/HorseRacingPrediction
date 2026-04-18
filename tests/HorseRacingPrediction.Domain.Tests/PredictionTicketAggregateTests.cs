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
    public void FullLifecycle_ProducesCorrectState()
    {
        var sut = new PredictionTicketAggregate(PredictionTicketId.New);

        sut.Create("race-abc", "AI", "model-v1", 0.92m, "精密予測");
        sut.AddMark("entry-1", "◎", 1, 95.0m, "本命");
        sut.AddMark("entry-2", "○", 2, 80.0m, "対抗");
        sut.AddMark("entry-3", "▲", 3, 65.0m, null);

        var details = sut.GetDetails();
        Assert.AreEqual("race-abc", details.RaceId);
        Assert.AreEqual(0.92m, details.ConfidenceScore);
        Assert.AreEqual("精密予測", details.SummaryComment);
        Assert.AreEqual(3, details.Marks.Count);
    }
}

using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public class RaceAggregateTests
{
    [TestMethod]
    public void Create_SetsRaceDetailsCorrectly()
    {
        var sut = new RaceAggregate(RaceId.New);

        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        var details = sut.GetDetails();
        Assert.AreEqual(new DateOnly(2025, 6, 15), details.RaceDate);
        Assert.AreEqual("TOKYO", details.RacecourseCode);
        Assert.AreEqual(5, details.RaceNumber);
        Assert.AreEqual("皐月賞", details.RaceName);
        Assert.AreEqual(RaceStatus.Draft, details.Status);
    }

    [TestMethod]
    public void Create_WhenAlreadyCreated_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.Create(new DateOnly(2025, 7, 1), "NAKAYAMA", 1, "有馬記念"));
    }

    [TestMethod]
    public void PublishCard_FromDraft_SetsCardPublishedStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        sut.PublishCard(18);

        var details = sut.GetDetails();
        Assert.AreEqual(RaceStatus.CardPublished, details.Status);
        Assert.AreEqual(18, details.EntryCount);
    }

    [TestMethod]
    public void PublishCard_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);

        Assert.ThrowsException<InvalidOperationException>(() => sut.PublishCard(18));
    }

    [TestMethod]
    public void PublishCard_WhenAlreadyPublished_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);

        Assert.ThrowsException<InvalidOperationException>(() => sut.PublishCard(16));
    }

    [TestMethod]
    public void DeclareResult_FromCardPublished_SetsResultDeclaredStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        var declaredAt = DateTimeOffset.UtcNow;

        sut.DeclareResult("ディープインパクト", declaredAt);

        var details = sut.GetDetails();
        Assert.AreEqual(RaceStatus.ResultDeclared, details.Status);
        Assert.AreEqual("ディープインパクト", details.WinningHorseName);
        Assert.AreEqual(declaredAt, details.ResultDeclaredAt);
    }

    [TestMethod]
    public void DeclareResult_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void DeclareResult_FromDraft_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void DeclareResult_FromResultDeclared_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeclareResult("キタサンブラック", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void GetDetails_ReturnsCorrectAggregateId()
    {
        var sut = new RaceAggregate(RaceId.New);
        Assert.AreEqual(sut.Id.Value, sut.GetDetails().RaceId);
    }

    [TestMethod]
    public void FullLifecycle_ProducesCorrectState()
    {
        var sut = new RaceAggregate(RaceId.New);
        var declaredAt = DateTimeOffset.UtcNow;

        sut.Create(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念");
        sut.PublishCard(16);
        sut.DeclareResult("イクイノックス", declaredAt);

        var details = sut.GetDetails();
        Assert.AreEqual(new DateOnly(2025, 12, 28), details.RaceDate);
        Assert.AreEqual("NAKAYAMA", details.RacecourseCode);
        Assert.AreEqual(11, details.RaceNumber);
        Assert.AreEqual("有馬記念", details.RaceName);
        Assert.AreEqual(RaceStatus.ResultDeclared, details.Status);
        Assert.AreEqual(16, details.EntryCount);
        Assert.AreEqual("イクイノックス", details.WinningHorseName);
        Assert.AreEqual(declaredAt, details.ResultDeclaredAt);
    }
}

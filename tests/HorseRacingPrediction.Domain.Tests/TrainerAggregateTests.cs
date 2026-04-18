using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public class TrainerAggregateTests
{
    [TestMethod]
    public void RegisterTrainer_SetsDetailsCorrectly()
    {
        var sut = new TrainerAggregate(TrainerId.New);

        sut.RegisterTrainer("池江泰寿", "いけえやすとし", affiliationCode: "栗東");

        var details = sut.GetDetails();
        Assert.AreEqual("池江泰寿", details.DisplayName);
        Assert.AreEqual("いけえやすとし", details.NormalizedName);
        Assert.AreEqual("栗東", details.AffiliationCode);
    }

    [TestMethod]
    public void RegisterTrainer_WhenAlreadyRegistered_ThrowsInvalidOperationException()
    {
        var sut = new TrainerAggregate(TrainerId.New);
        sut.RegisterTrainer("池江泰寿", "いけえやすとし");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.RegisterTrainer("藤原英昭", "ふじわらひであき"));
    }

    [TestMethod]
    public void UpdateProfile_UpdatesSpecifiedFields()
    {
        var sut = new TrainerAggregate(TrainerId.New);
        sut.RegisterTrainer("池江泰寿", "いけえやすとし", affiliationCode: "栗東");

        sut.UpdateProfile(affiliationCode: "美浦");

        Assert.AreEqual("美浦", sut.GetDetails().AffiliationCode);
        Assert.AreEqual("池江泰寿", sut.GetDetails().DisplayName);
    }

    [TestMethod]
    public void UpdateProfile_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new TrainerAggregate(TrainerId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.UpdateProfile(displayName: "テスト"));
    }

    [TestMethod]
    public void MergeAlias_AddsAlias()
    {
        var sut = new TrainerAggregate(TrainerId.New);
        sut.RegisterTrainer("池江泰寿", "いけえやすとし");

        sut.MergeAlias("JRA_CODE", "T0001", "JRA", true);

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.Aliases.Count);
        var alias = details.Aliases.First();
        Assert.AreEqual("JRA_CODE", alias.AliasType);
        Assert.AreEqual("T0001", alias.AliasValue);
        Assert.IsTrue(alias.IsPrimary);
    }

    [TestMethod]
    public void MergeAlias_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new TrainerAggregate(TrainerId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.MergeAlias("JRA_CODE", "T0001", "JRA", true));
    }

    [TestMethod]
    public void CorrectData_UpdatesSpecifiedFields()
    {
        var sut = new TrainerAggregate(TrainerId.New);
        sut.RegisterTrainer("池江泰寿", "いけえやすとし");

        sut.CorrectData(displayName: "Ikee Yasutoshi", reason: "英語表記に修正");

        Assert.AreEqual("Ikee Yasutoshi", sut.GetDetails().DisplayName);
    }

    [TestMethod]
    public void CorrectData_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new TrainerAggregate(TrainerId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.CorrectData(displayName: "テスト"));
    }

    [TestMethod]
    public void GetDetails_ReturnsCorrectAggregateId()
    {
        var sut = new TrainerAggregate(TrainerId.New);
        Assert.AreEqual(sut.Id.Value, sut.GetDetails().TrainerId);
    }

    [TestMethod]
    public void FullLifecycle_ProducesCorrectState()
    {
        var sut = new TrainerAggregate(TrainerId.New);

        sut.RegisterTrainer("池江泰寿", "いけえやすとし", affiliationCode: "栗東");
        sut.MergeAlias("JRA_CODE", "T0001", "JRA", true);
        sut.UpdateProfile(normalizedName: "ikee yasutoshi");
        sut.CorrectData(affiliationCode: "美浦");

        var details = sut.GetDetails();
        Assert.AreEqual("池江泰寿", details.DisplayName);
        Assert.AreEqual("ikee yasutoshi", details.NormalizedName);
        Assert.AreEqual("美浦", details.AffiliationCode);
        Assert.AreEqual(1, details.Aliases.Count);
    }
}

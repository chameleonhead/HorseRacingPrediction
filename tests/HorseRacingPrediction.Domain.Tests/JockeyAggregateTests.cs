using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public class JockeyAggregateTests
{
    [TestMethod]
    public void RegisterJockey_SetsDetailsCorrectly()
    {
        var sut = new JockeyAggregate(JockeyId.New);

        sut.RegisterJockey("武豊", "たけゆたか", affiliationCode: "JRA");

        var details = sut.GetDetails();
        Assert.AreEqual("武豊", details.DisplayName);
        Assert.AreEqual("たけゆたか", details.NormalizedName);
        Assert.AreEqual("JRA", details.AffiliationCode);
    }

    [TestMethod]
    public void RegisterJockey_WhenAlreadyRegistered_ThrowsInvalidOperationException()
    {
        var sut = new JockeyAggregate(JockeyId.New);
        sut.RegisterJockey("武豊", "たけゆたか");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.RegisterJockey("ルメール", "るめーる"));
    }

    [TestMethod]
    public void UpdateProfile_UpdatesSpecifiedFields()
    {
        var sut = new JockeyAggregate(JockeyId.New);
        sut.RegisterJockey("武豊", "たけゆたか", affiliationCode: "JRA");

        sut.UpdateProfile(affiliationCode: "FREE");

        Assert.AreEqual("FREE", sut.GetDetails().AffiliationCode);
        Assert.AreEqual("武豊", sut.GetDetails().DisplayName);
    }

    [TestMethod]
    public void UpdateProfile_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new JockeyAggregate(JockeyId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.UpdateProfile(displayName: "テスト"));
    }

    [TestMethod]
    public void MergeAlias_AddsAlias()
    {
        var sut = new JockeyAggregate(JockeyId.New);
        sut.RegisterJockey("武豊", "たけゆたか");

        sut.MergeAlias("JRA_CODE", "00001", "JRA", true);

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.Aliases.Count);
        var alias = details.Aliases.First();
        Assert.AreEqual("JRA_CODE", alias.AliasType);
        Assert.AreEqual("00001", alias.AliasValue);
        Assert.IsTrue(alias.IsPrimary);
    }

    [TestMethod]
    public void MergeAlias_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new JockeyAggregate(JockeyId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.MergeAlias("JRA_CODE", "00001", "JRA", true));
    }

    [TestMethod]
    public void CorrectData_UpdatesSpecifiedFields()
    {
        var sut = new JockeyAggregate(JockeyId.New);
        sut.RegisterJockey("武豊", "たけゆたか");

        sut.CorrectData(displayName: "Take Yutaka", reason: "英語表記に修正");

        Assert.AreEqual("Take Yutaka", sut.GetDetails().DisplayName);
    }

    [TestMethod]
    public void CorrectData_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new JockeyAggregate(JockeyId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.CorrectData(displayName: "テスト"));
    }

    [TestMethod]
    public void GetDetails_ReturnsCorrectAggregateId()
    {
        var sut = new JockeyAggregate(JockeyId.New);
        Assert.AreEqual(sut.Id.Value, sut.GetDetails().JockeyId);
    }

    [TestMethod]
    public void FullLifecycle_ProducesCorrectState()
    {
        var sut = new JockeyAggregate(JockeyId.New);

        sut.RegisterJockey("武豊", "たけゆたか", affiliationCode: "JRA");
        sut.MergeAlias("JRA_CODE", "00001", "JRA", true);
        sut.UpdateProfile(normalizedName: "take yutaka");
        sut.CorrectData(affiliationCode: "栗東");

        var details = sut.GetDetails();
        Assert.AreEqual("武豊", details.DisplayName);
        Assert.AreEqual("take yutaka", details.NormalizedName);
        Assert.AreEqual("栗東", details.AffiliationCode);
        Assert.AreEqual(1, details.Aliases.Count);
    }
}

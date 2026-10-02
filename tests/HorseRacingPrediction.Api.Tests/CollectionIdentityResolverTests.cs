using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionIdentityResolverTests
{
    private const string OfficialIdentity =
        "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002020123456/00";

    [TestMethod]
    public void OfficialIdentity_ReusesSingleSourceLessNameDerivedHorse()
    {
        const string name = "ゴールドドリーム";
        var legacyId = DeterministicIdGenerator.BuildHorseId(name);
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(legacyId, name, new DateOnly(2013, 4, 19), null, true),
        };

        var resolved = CollectionIdentityResolver.ResolveHorse(
            horses, name, OfficialIdentity, new DateOnly(2013, 4, 19));

        Assert.AreEqual(legacyId, resolved);
    }

    [TestMethod]
    public void OfficialIdentity_RejectsMultipleLegacyCandidates()
    {
        const string name = "同名馬";
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow("legacy-a", name, null, null, true),
            new CollectionIdentityResolver.HorseIdentityRow("legacy-b", name, null, null, true),
        };

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CollectionIdentityResolver.ResolveHorse(horses, name, OfficialIdentity, null));

        Assert.AreEqual("AmbiguousHorseIdentity", error.Message);
    }

    [TestMethod]
    public void OfficialIdentity_RejectsConflictingBirthDate()
    {
        const string name = "生年月日不一致馬";
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(
                DeterministicIdGenerator.BuildHorseId(name), name, new DateOnly(2018, 1, 1), null, true),
        };

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CollectionIdentityResolver.ResolveHorse(
                horses, name, OfficialIdentity, new DateOnly(2019, 1, 1)));

        Assert.AreEqual("HorseIdentityConflict", error.Message);
    }

    [TestMethod]
    public void OfficialIdentity_RejectsSameNameSourceBoundHorseWithDifferentIdentity()
    {
        const string name = "公式識別子競合馬";
        const string otherIdentity =
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002020654321/00";
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(
                DeterministicIdGenerator.BuildHorseId(name, otherIdentity), name, null, otherIdentity),
        };

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CollectionIdentityResolver.ResolveHorse(horses, name, OfficialIdentity, null));

        Assert.AreEqual("HorseIdentityConflict", error.Message);
    }

    [TestMethod]
    public void OfficialIdentity_ReusesSourceBoundHorseAcrossJraRouteVariants()
    {
        const string name = "ゴールドドリーム";
        const string profileIdentity =
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002013106119/DD";
        const string resultIdentity =
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud102013106119/C4";
        var horseId = DeterministicIdGenerator.BuildHorseId(name);
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(
                horseId, name, new DateOnly(2013, 4, 19), profileIdentity, true),
        };

        var resolved = CollectionIdentityResolver.ResolveHorse(
            horses, name, resultIdentity, new DateOnly(2013, 4, 19));

        Assert.AreEqual(horseId, resolved);
    }
}

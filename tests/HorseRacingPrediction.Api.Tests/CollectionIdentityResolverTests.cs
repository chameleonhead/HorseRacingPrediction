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
    public void OfficialIdentity_ReusesSingleSourceLessOpaqueLegacyHorse()
    {
        const string name = "旧世代ID馬";
        const string legacyId = "horse-60c4cf5b-0233-4dd2-b7d0-5fc378f67ab0";
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(
                legacyId, name, new DateOnly(2021, 3, 12), null),
        };

        var resolved = CollectionIdentityResolver.ResolveHorse(
            horses, name, OfficialIdentity, new DateOnly(2021, 3, 12));

        Assert.AreEqual(legacyId, resolved);
    }

    [TestMethod]
    public void OfficialIdentity_ChoosesStableHorseBesideNameDerivedDuplicateAcrossJraRoutes()
    {
        const string name = "ゴディアンフィンチ";
        const string stableId = "horse-35add83f-ed13-5299-8970-e0d8939fee9f";
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(
                "horse-1f00e5ee-3a55-528b-af51-c9370ae52ddb", name, null, null, true),
            new CollectionIdentityResolver.HorseIdentityRow(
                stableId, name, new DateOnly(2023, 1, 24),
                "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002023103764/B2"),
        };

        var resolved = CollectionIdentityResolver.ResolveHorse(horses, name,
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud102023103764/99", null);

        Assert.AreEqual(stableId, resolved);
    }

    [TestMethod]
    public void OfficialIdentity_RejectsMultipleSourceLessOpaqueLegacyHorses()
    {
        const string name = "旧世代同名馬";
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(
                "horse-b30f7434-18d2-46ad-b539-5caf8787b7be", name, null, null),
            new CollectionIdentityResolver.HorseIdentityRow(
                "horse-bbd5b513-c71f-4e96-a1aa-251211df29b2", name, null, null),
        };

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CollectionIdentityResolver.ResolveHorse(horses, name, OfficialIdentity, null));

        Assert.AreEqual("AmbiguousHorseIdentity", error.Message);
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
    public void OfficialIdentity_AllowsSameNameSourceBoundHorseWithDifferentIdentity()
    {
        const string name = "公式識別子競合馬";
        const string otherIdentity =
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002020654321/00";
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(
                DeterministicIdGenerator.BuildHorseId(name, otherIdentity), name, null, otherIdentity),
        };

        var resolved = CollectionIdentityResolver.ResolveHorse(horses, name, OfficialIdentity, null);

        Assert.AreEqual(DeterministicIdGenerator.BuildHorseId(name, OfficialIdentity), resolved);
    }

    [TestMethod]
    public void OfficialIdentity_RejectsSourceLessCandidateAlongsideSourceBoundNamesake()
    {
        const string name = "曖昧な同名馬";
        const string otherIdentity =
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002020654321/00";
        var horses = new[]
        {
            new CollectionIdentityResolver.HorseIdentityRow(
                DeterministicIdGenerator.BuildHorseId(name), name, null, null, true),
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

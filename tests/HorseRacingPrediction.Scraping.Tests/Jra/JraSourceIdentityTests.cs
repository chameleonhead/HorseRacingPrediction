
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

[TestClass]
public sealed class JraSourceIdentityTests
{
    [TestMethod]
    [DataRow("//example.com/JRADB/accessU.html?CNAME=one")]
    [DataRow("file:///JRADB/accessU.html?CNAME=one")]
    [DataRow("about:/JRADB/accessU.html?CNAME=one")]
    public void HorseIdentity_RejectsNonJraOriginsAfterRelativeResolution(string url)
    {
        Assert.IsFalse(JraSourceIdentity.TryNormalizeHorse(url, out _));
    }

    [TestMethod]
    public void HorseIdentity_NormalizesRelativeAndAbsoluteUrlsToTheSameId()
    {
        const string relative = "/JRADB/accessU.html?CNAME=pw01dud002023106188/45";
        const string absolute = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002023106188/45";

        Assert.IsTrue(JraSourceIdentity.TryNormalizeHorse(relative, out var first));
        Assert.IsTrue(JraSourceIdentity.TryNormalizeHorse(absolute, out var second));
        Assert.AreEqual(first, second);
        Assert.AreEqual(DeterministicIdGenerator.BuildHorseId("表記A", relative),
            DeterministicIdGenerator.BuildHorseId("表記B", absolute));
    }

    [TestMethod]
    public void HorseIdentity_NormalizesResultAndProfileRoutesToTheSameStableHorseNumber()
    {
        const string profile = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002013106119/DD";
        const string result = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud102013106119/C4";

        Assert.IsTrue(JraSourceIdentity.TryNormalizeHorse(profile, out var profileIdentity));
        Assert.IsTrue(JraSourceIdentity.TryNormalizeHorse(result, out var resultIdentity));
        Assert.AreEqual("jra-horse:2013106119", profileIdentity);
        Assert.AreEqual(profileIdentity, resultIdentity);
        Assert.IsTrue(JraSourceIdentity.MatchesHorse(profile, result));
        Assert.AreEqual(DeterministicIdGenerator.BuildHorseId("ゴールドドリーム", profile),
            DeterministicIdGenerator.BuildHorseId("ゴールドドリーム", result));
    }

    [TestMethod]
    public void NormalizeHorseUrl_PreservesTheNavigableCnameWhileIdentityUsesStableHorseNumber()
    {
        const string result = "/JRADB/accessU.html?CNAME=pw01dud102013106119/C4";

        var normalized = JraSourceIdentity.NormalizeHorseUrl(result);

        Assert.AreEqual("https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud102013106119/C4",
            normalized?.AbsoluteUri);
    }

    [TestMethod]
    public void HorseIdentity_RejectsMissingOrDuplicateCName()
    {
        Assert.IsFalse(JraSourceIdentity.TryNormalizeHorse("https://www.jra.go.jp/JRADB/accessU.html", out _));
        Assert.IsFalse(JraSourceIdentity.TryNormalizeHorse(
            "https://www.jra.go.jp/JRADB/accessU.html?CNAME=one&CNAME=two", out _));
    }

    [TestMethod]
    public void HorseIdentity_DifferentJraIdsRemainDifferentEvenWhenNamesMatch()
    {
        const string first = "/JRADB/accessU.html?CNAME=pw01dud002023106188/45";
        const string second = "/JRADB/accessU.html?CNAME=pw01dud002007101324/2A";

        Assert.AreNotEqual(DeterministicIdGenerator.BuildHorseId("ビッグヒーロー", first),
            DeterministicIdGenerator.BuildHorseId("ビッグヒーロー", second));
    }

    [TestMethod]
    public void HorseIdentity_ConcurrentResolutionReturnsOneCanonicalId()
    {
        const string url = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002023106188/45";

        var ids = Enumerable.Range(0, 64).AsParallel()
            .Select(_ => DeterministicIdGenerator.BuildHorseId("ビッグヒーロー", url))
            .Distinct(StringComparer.Ordinal).ToArray();

        Assert.HasCount(1, ids);
    }
}

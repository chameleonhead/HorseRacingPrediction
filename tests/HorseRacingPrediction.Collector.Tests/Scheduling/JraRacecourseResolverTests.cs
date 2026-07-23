using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class JraRacecourseResolverTests
{
    [TestMethod]
    public void Normalize_WhenOnlyRacecourseCodeIsPresent_SetsJapaneseDisplayName()
    {
        var source = JraRaceResultUrl.ParseFromUrl(
            "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1002202601050120260627/AB");

        var result = JraRacecourseResolver.Normalize(source);

        Assert.AreEqual("函館", result.Racecourse);
        Assert.AreEqual("02", result.RacecourseCode);
    }

    [TestMethod]
    public void Normalize_WhenRacecourseIsAlreadyPresent_PreservesCanonicalDisplayName()
    {
        var source = JraRaceResultUrl.ParseFromUrl(
            "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1003202602010120260627/5A",
            "福島");

        var result = JraRacecourseResolver.Normalize(source);

        Assert.AreSame(source, result);
        Assert.AreEqual("福島", result.Racecourse);
        Assert.AreEqual("03", result.RacecourseCode);
    }
}

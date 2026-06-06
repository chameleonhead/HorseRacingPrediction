using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public sealed class JraRaceCardUrlTests
{
    [TestMethod]
    public void ParseFromUrl_Pw01DdeUrl_ParsesRaceMetadata()
    {
        var url = "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde1008202603091120260523/DD";

        var parsed = JraRaceCardUrl.ParseFromUrl(url, "京都");

        Assert.AreEqual(new DateOnly(2026, 5, 23), parsed.RaceDate);
        Assert.AreEqual("08", parsed.RacecourseCode);
        Assert.AreEqual(11, parsed.RaceNumber);
        Assert.AreEqual("京都", parsed.Racecourse);
    }
}
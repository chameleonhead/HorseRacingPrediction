using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

[TestClass]
public sealed class JraRaceLinkSelectorTests
{
    private static readonly RaceId Expected =
        new(new DateOnly(2026, 9, 21), RaceCourse.Nakayama, 2);

    [TestMethod]
    public void FindUrl_ResultNavigation_PrefersNumberedDirectResultOverFragmentControls()
    {
        const string fragment = "https://www.jra.go.jp/JRADB/accessS.html#";
        const string direct =
            "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde0106202604070220260921/69";

        var selected = JraRaceLinkSelector.FindUrl(
            [
                (fragment, "検索"),
                (fragment, "2レース"),
                (direct, "2R"),
            ],
            raceNumber: 2,
            ["レース結果"],
            allowNumberOnly: true);

        Assert.AreEqual(direct, selected);
    }

    [TestMethod]
    public void FindLink_DoesNotCombinePurposeAndRaceLabelsAcrossSharedFragmentUrl()
    {
        const string fragment = "https://www.jra.go.jp/JRADB/accessS.html#";

        var selected = JraRaceLinkSelector.FindLink(
            [
                (fragment, "レース結果"),
                (fragment, "検索"),
                (fragment, "2レース"),
            ],
            raceNumber: 2,
            ["レース結果"],
            allowNumberOnly: true);

        Assert.IsNotNull(selected);
        Assert.AreEqual("2レース", selected.Value.Label);
    }
}

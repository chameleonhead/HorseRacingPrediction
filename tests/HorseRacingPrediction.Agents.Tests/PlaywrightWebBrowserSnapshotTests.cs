using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public sealed class PlaywrightWebBrowserSnapshotTests
{
    [TestMethod]
    public void DefaultLaunchOptions_AreSafeForServerlessChromium()
    {
        var options = PlaywrightWebBrowser.CreateDefaultLaunchOptions();

        Assert.IsTrue(options.Headless);
        Assert.IsFalse(options.ChromiumSandbox);
        var arguments = options.Args!.ToArray();
        CollectionAssert.Contains(arguments, "--disable-dev-shm-usage");
        CollectionAssert.Contains(arguments, "--no-zygote");
        CollectionAssert.Contains(arguments, "--single-process");
    }

    [TestMethod]
    public void IsTextCoveredByExistingSections_ReturnsTrueForAlreadyCapturedBlock()
    {
        var sections = CreateSections("3歳以上1勝クラス コース：1,700 メートル（ダート・右）");

        var covered = PlaywrightWebBrowser.IsTextCoveredByExistingSections(
            "コース：1,700 メートル（ダート・右）",
            sections);

        Assert.IsTrue(covered);
    }

    [TestMethod]
    public void IsTextCoveredByExistingSections_ReturnsFalseForUncapturedCourseBlock()
    {
        var sections = CreateSections("着順 馬番 馬名 騎手 タイム");

        var covered = PlaywrightWebBrowser.IsTextCoveredByExistingSections(
            "コース：1,700 メートル（ダート・右）",
            sections);

        Assert.IsFalse(covered);
    }

    private static IReadOnlyList<PageSectionSnapshot> CreateSections(string mainText)
        =>
        [
            new PageSectionSnapshot(
                title: "Result",
                mainText,
                headings: [],
                links: [],
                actions: [],
                tables: [])
        ];
}

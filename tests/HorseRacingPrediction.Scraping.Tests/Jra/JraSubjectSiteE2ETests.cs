using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Browser.Snapshots;

namespace HorseRacingPrediction.Scraping.Tests.Jra;

[TestClass, TestCategory("External")]
public sealed class JraSubjectSiteE2ETests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    public async Task HorseProfileAndHistoryReachTheReportedRace()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var browser = await PlaywrightWebBrowser.CreateAsync();
        var reader = new JraPageReader(browser, [new RaceResultPageParser()]);
        var navigator = new JraNavigator(browser, reader);
        var identity = new JraSubjectIdentity("Horse", "エンジャムメント", new DateOnly(2024, 4, 11));
        HorseRacingPrediction.Scraping.Jra.Pages.JraSubjectPage page;
        try { page = await navigator.ToSubjectProfileAsync(identity, timeout.Token); }
        catch
        {
            var snapshot = await browser.GetDataPageSnapshotAsync(timeout.Token);
            TestContext.WriteLine(snapshot.Url + "\n" + snapshot.Root.GetEffectiveText());
            throw;
        }
        Assert.AreEqual("栗毛", page.Profile.Fields["毛色"]);
        var race = page.Races.Single(r => r.Date == new DateOnly(2026, 9, 6) && r.Course == "中山");
        var result = await navigator.ToHorseHistoryResultAsync(identity, race, timeout.Token);
        Assert.AreEqual(6, result.RaceId.Number);
        Assert.AreEqual("メイクデビュー中山", result.RaceName);
        Assert.AreEqual(new TimeOnly(13, 0), result.StartTime);
        StringAssert.Contains(result.OverallPaceText!, "12.7 - 11.8");
        StringAssert.Contains(result.OverallPaceText!, "4F 50.2 - 3F 37.9");
        Assert.AreEqual("7(8,12)(10,9)11,4(1,6)(3,5)-2", result.CornerPassages![0].OrderRaw);
        Assert.AreEqual(4, result.CornerPassages[^1].CornerNumber);
    }

    [TestMethod]
    public async Task HorseProfileSearch_DaiyuVenti()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var browser = await PlaywrightWebBrowser.CreateAsync();
        var navigator = new JraNavigator(browser, new JraPageReader(browser, []));
        var page = await navigator.ToSubjectProfileAsync(new("Horse", "ダイユウヴェンティ"), timeout.Token);
        Assert.AreEqual(SubjectProfilePageParser.Normalize("ダイユウヴェンティ"),
            SubjectProfilePageParser.Normalize(page.Profile.Name));
        Assert.IsTrue(page.Profile.Fields.ContainsKey("生年月日"));
    }

    [TestMethod]
    public async Task TrainerProfileIsReachedFromPublicDirectory()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var browser = await PlaywrightWebBrowser.CreateAsync();
        var navigator = new JraNavigator(browser, new JraPageReader(browser, []));
        var page = await navigator.ToSubjectProfileAsync(new("Trainer", "中舘 英二"), timeout.Token);
        Assert.AreEqual("美浦", page.Profile.Fields["所属"]);
        Assert.AreEqual("2015年", page.Profile.Fields["免許取得年"]);
    }
}

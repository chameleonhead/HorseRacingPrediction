using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Models;

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
            var snapshot=await browser.GetDataPageSnapshotAsync(timeout.Token);
            TestContext.WriteLine(snapshot.Url+"\n"+snapshot.MainText);
            throw;
        }
        Assert.AreEqual("栗毛", page.Profile.Fields["毛色"]);
        var race = page.Races.Single(r=>r.Date == new DateOnly(2026,9,6) && r.Course == "中山");
        var result = await navigator.ToHorseHistoryResultAsync(identity, race, timeout.Token);
        Assert.AreEqual(6, result.RaceId.Number);
        Assert.AreEqual("メイクデビュー中山", result.RaceName);
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

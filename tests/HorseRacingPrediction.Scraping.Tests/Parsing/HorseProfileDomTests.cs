using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class HorseProfileDomTests
{
    [TestMethod]
    [DataRow("ディープインパクト", "Deep Impact（JPN）", "マルイチ")]
    [DataRow("アジアエクスプレス", "Asia Express（USA）", "マルガイ")]
    [DataRow("Corniche", "Corniche（USA）", "")]
    public async Task DirectNameText_ExcludesLabelsImageAltAndEnglish(string name, string english, string mark)
    {
        var snapshot = await Capture($"<span class='txt'><span class='opt'>競走馬情報</span><span class='horse_icon'><img alt='{mark}'></span>{name}<span class='name_en'>{english}</span></span><span class='opt'>抹消年月日</span>");
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<PageSnapshot>(System.Text.Json.JsonSerializer.Serialize(snapshot))!;
        Assert.AreEqual(name, SubjectProfilePageParser.Parse(roundTrip, "Horse").Profile.Name);
    }

    [TestMethod]
    [DataRow("競走馬情報 ディープインパクト")]
    [DataRow("<span class='opt'>競走馬情報</span><span class='txt'>馬A</span><span class='txt'>馬B</span>")]
    public async Task MissingOrAmbiguousDomName_IsNotFlattened(string heading)
    {
        var snapshot = await Capture(heading);
        Assert.ThrowsExactly<JraCollectionException>(() => SubjectProfilePageParser.Parse(snapshot, "Horse"));
    }

    private static async Task<PageSnapshot> Capture(string heading)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync($"<main><h1><span class='inner'>{heading}</span></h1><dl><dt>生年月日</dt><dd>2002年3月25日</dd></dl></main>");
        return await new PlaywrightPageSnapshotter().CaptureAsync(page);
    }
}

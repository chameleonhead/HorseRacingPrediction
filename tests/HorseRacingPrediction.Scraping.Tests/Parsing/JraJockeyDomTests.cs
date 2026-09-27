using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class JraJockeyDomTests
{
    [TestMethod]
    [DataRow("幸 英明", "106 M")]
    [DataRow("浜中 俊", "105 S,M")]
    [DataRow("C.ルメール", "116 M")]
    [DataRow("▲ 森田 誠也", "")]
    public async Task Card_SelectsDedicatedParagraphWithoutRatingPastRiderOrPostUrlGuess(string name, string rating)
    {
        var snapshot = await Capture($"<p class='jockey'><a href='#' onclick=\"return doAction('/JRADB/accessK.html', 'opaque');\">{name}</a></p><div class='rating'>{rating}</div>");
        var entry = ((JraRaceCardPage)new RaceCardPageParser().Parse(snapshot)).Entries.Single();
        Assert.AreEqual(name.Replace("▲ ", ""), entry.JockeyName);
        Assert.IsNull(entry.JockeyProfileUrl);
        Assert.AreEqual(56m, entry.AssignedWeight);
    }

    [TestMethod]
    [DataRow("<div>幸 英明 106 M</div>")]
    [DataRow("<p class='jockey'>幸 英明</p><p class='jockey'>浜中 俊</p>")]
    public async Task CombinedCell_MissingOrAmbiguousParagraphNeverFallsBackToCellTail(string contents)
    {
        var snapshot = await Capture(contents);
        var ex = Assert.ThrowsExactly<JraPageStructureException>(() => new RaceCardPageParser().Parse(snapshot));
        Assert.AreEqual("JockeyName", ex.FieldName);
    }

    private static async Task<PageSnapshot> Capture(string contents)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync($"""
            <main><h1>2026年9月27日 中山 11レース</h1><h2>テストステークス</h2><p>発走 15:40</p>
            <table><thead><tr><th>馬番</th><th>馬名</th><th>性齢/毛色 負担重量 騎手名 プレレーティング</th><th>前走</th></tr></thead>
            <tbody><tr><td>2</td><td>アイサンサン</td>
            <td class="jockey"><p class="age">牝4/青鹿</p><p class="weight">56.0<span>kg</span></p>{contents}</td>
            <td><div class="jockey">別の過去騎手</div><span>18頭15番</span></td></tr></tbody></table></main>
            """);
        return await new PlaywrightPageSnapshotter().CaptureAsync(page);
    }

    [TestMethod]
    public async Task TruncatedFragments_DoNotAssertNameUniqueness()
    {
        var snapshot = await Capture("<p class='jockey'>幸 英明</p>");
        snapshot = snapshot with { Diagnostics = [new(PageSnapshotDiagnosticSeverity.Warning, "table-cell-fragments-truncated", "test")] };
        Assert.ThrowsExactly<JraPageStructureException>(() => new RaceCardPageParser().Parse(snapshot));
    }
}

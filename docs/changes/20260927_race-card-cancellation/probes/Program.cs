using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Playwright;

// Read-only incident reproduction. No administration API, persistence or recovery calls.
const string url = "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604090120260927/08";
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var html = await client.GetByteArrayAsync(url);
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync();
// Keep the actual URL for source identity, but serve only the fetched HTML and block subresources.
await page.RouteAsync("**/*", async route =>
{
    if (route.Request.IsNavigationRequest && route.Request.Url == url)
        await route.FulfillAsync(new() { BodyBytes = html, ContentType = "text/html" });
    else
        await route.AbortAsync();
});
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
var rows = await page.Locator("td.num").AllTextContentsAsync();
Console.WriteLine("Observed number cells: " + string.Join(",", rows.Select(x => x.Trim())));
var snapshot = await new PlaywrightPageSnapshotter().CaptureAsync(page);
try
{
    var card = (JraRaceCardPage)new RaceCardPageParser().Parse(snapshot);
    var cancelled = card.Entries.Single(x => x.ParticipationStatus == HorseRacingPrediction.Contracts.RaceEntryParticipationStatus.Cancelled);
    if (card.Entries.Count != 16 || cancelled.HorseName != "ニシノドリーマー"
        || cancelled.HorseNumber is not null || cancelled.OwnerName != "西山 茂行"
        || string.IsNullOrWhiteSpace(cancelled.HorseSourceIdentity))
        throw new InvalidOperationException("Live card does not match the approved incident acceptance criteria.");
    Console.WriteLine($"VERIFIED: Entries={card.Entries.Count}; Active={card.Entries.Count(x => x.ParticipationStatus == HorseRacingPrediction.Contracts.RaceEntryParticipationStatus.Active)}; Cancelled={cancelled.HorseName}; Number=null; Owner={cancelled.OwnerName}");
}
catch (JraValueParseException exception)
{
    Console.WriteLine($"REPRODUCED: {exception.GetType().Name}; Field={exception.FieldName}; Raw={exception.RawValue}");
    throw;
}

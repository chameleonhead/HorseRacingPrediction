using System.Text.Json;
using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Playwright;

if (args.Length != 1) throw new ArgumentException("Specify one JRA result URL.");
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync();
await page.GotoAsync(args[0], new() { WaitUntil = WaitUntilState.DOMContentLoaded });
var snapshot = await new PlaywrightPageSnapshotter().CaptureAsync(page);
var result = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);
Console.WriteLine(JsonSerializer.Serialize(new
{
    result.RaceId,
    Entries = result.Results.Select(entry => new
    {
        entry.HorseNumber,
        entry.HorseName,
        entry.HorseSourceIdentity,
    }),
}, new JsonSerializerOptions { WriteIndented = true }));

using System.Text.Json;
using Microsoft.Playwright;
using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.ApiClient;

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync();
foreach (var url in args.Where(arg => arg != "--compact"))
{
    try
    {
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var snapshot = await new PlaywrightPageSnapshotter().CaptureAsync(page);
        object parsed;
        try { parsed = url.Contains("accessS") ? new RaceResultPageParser().Parse(snapshot) : url.Contains("accessU") ? SubjectProfilePageParser.Parse(snapshot, "Horse") : url.Contains("accessD") ? new RaceCardPageParser().Parse(snapshot) : new { directoryOnly = true }; }
        catch (Exception ex) { parsed = new { error = ex.GetType().Name, ex.Message }; }
        if (args.Contains("--compact"))
        {
            object compact = parsed is JraRaceCardPage card ? new
            {
                card.RaceId, entries = card.Entries.Select(entry => new
                {
                    entry.HorseName, entry.HorseNumber, entry.HorseSourceIdentity, entry.JockeyName,
                    horseId = DeterministicIdGenerator.BuildHorseId(entry.HorseName, entry.HorseSourceIdentity),
                    jockeyId = DeterministicIdGenerator.BuildEntityId("jockey", DeterministicIdGenerator.NormalizeKey(
                        HorseRacingPrediction.Contracts.JraSubjectNameNormalizer.CanonicalizeDisplayName("Jockey", entry.JockeyName!))),
                })
            } : parsed is JraSubjectPage subject ? new { subject.Profile.Name, subject.Profile.SourceIdentity } : parsed;
            Console.WriteLine(JsonSerializer.Serialize(new { url, capturedAt = DateTimeOffset.UtcNow, parsed = compact },
                new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            continue;
        }
        var riderDom = await page.EvaluateAsync<string>("""
            () => JSON.stringify(Array.from(document.querySelectorAll('td.jockey')).map(c => ({
                names: Array.from(c.querySelectorAll('p.jockey')).map(p => p.innerText),
                rating:c.querySelector('.rating')?.innerText
            })))
            """);
        var otherDom = await page.EvaluateAsync<string>("""
            () => JSON.stringify({
                subjectHeadings:Array.from(document.querySelectorAll('h1 .txt')).map(e => ({
                    html:e.innerHTML,
                    directText:Array.from(e.childNodes).filter(n=>n.nodeType===Node.TEXT_NODE).map(n=>n.textContent).join('').trim()
                })),
                retiredLinks:Array.from(document.querySelectorAll('a')).filter(a=>a.innerText.includes('引退')).map(a=>({text:a.innerText,url:a.href}))
            })
            """);
        object? injectedFailure = null;
        if (url.Contains("accessS"))
        {
            var changed = await page.EvaluateAsync<bool>("""
                () => {
                    const row = Array.from(document.querySelectorAll('tr')).find(r => r.innerText.includes('エトワールハマー'));
                    const cell = row && Array.from(row.children).find(c => c.innerText.trim() === '2:01.2');
                    if (!cell) return false;
                    cell.replaceChildren(); return true;
                }
                """);
            if (changed)
                try { new RaceResultPageParser().Parse(await new PlaywrightPageSnapshotter().CaptureAsync(page)); }
                catch (Exception ex) { injectedFailure = new { error = ex.GetType().Name, ex.Message }; }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { url, capturedAt = DateTimeOffset.UtcNow, riderDom = JsonSerializer.Deserialize<JsonElement>(riderDom), otherDom = JsonSerializer.Deserialize<JsonElement>(otherDom), snapshot.Diagnostics, parsed, injectedFailure }));
    }
    catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { url, error = ex.ToString() })); }
}

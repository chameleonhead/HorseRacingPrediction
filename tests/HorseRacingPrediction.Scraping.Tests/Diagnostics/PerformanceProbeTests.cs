using System.Text;
using HorseRacingPrediction.Scraping.Browser.Diagnostics;
using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Tests.Diagnostics;

[TestClass]
public sealed class PerformanceProbeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void StaticProtocolBaseline_FreezesCurrentSourceBackedScenarios()
    {
        Assert.AreEqual(701, BrowserProtocolBaseline.LinkExtractionHappyPath(100));
        Assert.AreEqual(1301, BrowserProtocolBaseline.LinkExtractionWorstTextFallback(100));
        Assert.AreEqual(304, BrowserProtocolBaseline.ClickLinkHappyPath(99));
        Assert.AreEqual(311, BrowserProtocolBaseline.ClickableDiscoveryHappyPath(100, 5));
        Assert.AreEqual(279, BrowserProtocolBaseline.FormExtractionHappyPath(2, 20, 2, 24));
    }

    [TestMethod]
    public async Task LocalPipelineBenchmark_ReportsReadyTextCaptureViewParserNavigationRepositoryAndAll()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        var snapshotter = new PlaywrightPageSnapshotter();
        var parser = new RaceListPageParser();
        var counter = new OperationCounter();
        var samples = new List<PerformanceSample>();
        var uri = "data:text/html;charset=utf-8," + Uri.EscapeDataString(BuildRaceListHtml());

        await page.GotoAsync(uri, new() { WaitUntil = WaitUntilState.Load });
        var warmSnapshot = await snapshotter.CaptureAsync(page);
        _ = JraSnapshotView.Create(warmSnapshot);
        _ = parser.Parse(warmSnapshot);

        for (var iteration = 0; iteration < 5; iteration++)
        {
            PageSnapshot snapshot = null!;
            samples.Add(await PerformanceProbe.MeasureAsync("all", async () =>
            {
                samples.Add(await PerformanceProbe.MeasureAsync("navigation", async () =>
                    await page.GotoAsync(uri, new() { WaitUntil = WaitUntilState.DOMContentLoaded })));
                samples.Add(await PerformanceProbe.MeasureAsync("ready", async () =>
                    await page.WaitForFunctionAsync("() => document.readyState === 'complete'", null,
                        new() { Timeout = 3_000 })));
                samples.Add(await PerformanceProbe.MeasureAsync("text", async () =>
                    _ = await page.Locator("body").InnerTextAsync()));
                samples.Add(await PerformanceProbe.MeasureAsync("capture", async () =>
                    snapshot = await snapshotter.CaptureAsync(page)));
                samples.Add(PerformanceProbe.Measure("view", () => _ = JraSnapshotView.Create(snapshot)));
                samples.Add(PerformanceProbe.Measure("parser", () => _ = parser.Parse(snapshot)));
                samples.Add(await PerformanceProbe.MeasureAsync("repository-call", () =>
                {
                    counter.Increment("repository-call");
                    return Task.CompletedTask;
                }));
            }));
        }

        Assert.AreEqual(5, counter.Get("repository-call"));
        Assert.HasCount(35, samples.Where(sample => sample.Stage != "all"));
        foreach (var stage in samples.Select(sample => sample.Stage).Distinct().Order())
        {
            var summary = PerformanceProbe.Summarize(stage, samples);
            TestContext.WriteLine($"{stage}: iterations={summary.Iterations}; meanMs={summary.MeanElapsedMilliseconds:F3}; p50Ms={summary.P50ElapsedMilliseconds:F3}; p95Ms={summary.P95ElapsedMilliseconds:F3}; meanApproxBytes={summary.MeanApproximateAllocatedBytes}; p95ApproxBytes={summary.P95ApproximateAllocatedBytes}");
            Assert.AreEqual(5, summary.Iterations);
            Assert.IsGreaterThanOrEqualTo(0, summary.P95ApproximateAllocatedBytes);
        }
    }

    [TestMethod]
    public void Summarize_ReportsDeterministicNearestRankPercentiles()
    {
        var samples = Enumerable.Range(1, 5)
            .Select(value => new PerformanceSample("parser", TimeSpan.FromMilliseconds(value), value * 10L));

        var summary = PerformanceProbe.Summarize("parser", samples);

        Assert.AreEqual(5, summary.Iterations);
        Assert.AreEqual(3, summary.P50ElapsedMilliseconds);
        Assert.AreEqual(5, summary.P95ElapsedMilliseconds);
        Assert.AreEqual(50, summary.P95ApproximateAllocatedBytes);
    }

    private static string BuildRaceListHtml()
    {
        var rows = new StringBuilder();
        for (var number = 1; number <= 12; number++)
            rows.Append($"<tr><td>{number}R</td><td>{9 + number / 2:D2}:{number % 2 * 30:D2}</td><td><a href='/race/{number}'>計測レース{number}</a></td></tr>");
        return $"<!doctype html><html lang='ja'><head><title>2026年9月15日 中山 レース一覧</title></head><body><main><h1>2026年9月15日 中山 レース一覧</h1><table><thead><tr><th>R</th><th>発走時刻</th><th>レース名</th></tr></thead><tbody>{rows}</tbody></table></main></body></html>";
    }
}

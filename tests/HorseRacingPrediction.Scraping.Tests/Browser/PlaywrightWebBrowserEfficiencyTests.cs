using System.Diagnostics;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Tests.Browser;

[TestClass]
public sealed class PlaywrightWebBrowserEfficiencyTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(10)]
    [DataRow(100)]
    [DataRow(500)]
    public async Task GetLinksAsync_BatchExtractsAllCandidates(int count)
    {
        var path = WriteFixture(string.Concat(Enumerable.Range(0, count)
            .Select(index => $"<a href='target-{index}.html'>Link {index}</a>")));
        try
        {
            await using var browser = await PlaywrightWebBrowser.CreateAsync();
            await browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri);

            var links = await browser.GetLinksAsync(count);

            Assert.HasCount(count, links);
            Assert.AreEqual("Link 0", links[0].Title);
            Assert.AreEqual($"Link {count - 1}", links[^1].Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task NavigateForSnapshotAsync_DoesNotWaitForSlowImageOrReadLegacyText()
    {
        var path = WriteFixture("<main><h1>Ready</h1></main><img src='http://10.255.255.1/never.png' alt='slow'>");
        try
        {
            await using var browser = await PlaywrightWebBrowser.CreateAsync();
            var startedAt = Stopwatch.GetTimestamp();

            await browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri);
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var snapshot = await browser.CapturePageSnapshotAsync();

            Assert.IsLessThan(TimeSpan.FromSeconds(3), elapsed);
            Assert.Contains("Ready", JraSnapshotView.Create(snapshot).Headings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ClickForSnapshotAsync_EqualBestCandidatesFailsWithoutClickingEither()
    {
        var path = WriteFixture("<main id='value'>unchanged</main><button onclick=\"value.textContent='first'\">Open</button><button onclick=\"value.textContent='second'\">Open</button>");
        try
        {
            await using var browser = await PlaywrightWebBrowser.CreateAsync();
            await browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri);

            await Assert.ThrowsAsync<InvalidOperationException>(() => browser.ClickForSnapshotAsync("Open"));
            var snapshot = await browser.CapturePageSnapshotAsync();

            Assert.Contains("unchanged", JraSnapshotView.Create(snapshot).MainText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task GetLinksAsync_OneHundredLinks_IsAtLeastEightyPercentFasterThanLocatorLoop()
    {
        const int count = 100;
        var path = WriteFixture(string.Concat(Enumerable.Range(0, count)
            .Select(index => $"<a href='target-{index}.html' aria-label='Link {index}'>Link {index}</a>")));
        try
        {
            var uri = new Uri(path).AbsoluteUri;
            using var playwright = await Playwright.CreateAsync();
            await using var rawBrowser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var rawPage = await rawBrowser.NewPageAsync();
            await rawPage.GotoAsync(uri);
            await using var optimized = await PlaywrightWebBrowser.CreateAsync();
            await optimized.NavigateForSnapshotAsync(uri);

            await MeasureLegacyAsync(rawPage);
            _ = await optimized.GetLinksAsync(count);
            var legacy = new List<double>();
            var batch = new List<double>();
            for (var iteration = 0; iteration < 5; iteration++)
            {
                var start = Stopwatch.GetTimestamp();
                await MeasureLegacyAsync(rawPage);
                legacy.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                start = Stopwatch.GetTimestamp();
                _ = await optimized.GetLinksAsync(count);
                batch.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            var legacyMedian = legacy.Order().ElementAt(2);
            var batchMedian = batch.Order().ElementAt(2);
            var improvement = 1 - batchMedian / legacyMedian;
            TestContext.WriteLine($"legacyP50Ms={legacyMedian:F3}; batchP50Ms={batchMedian:F3}; improvement={improvement:P1}");
            Assert.IsGreaterThanOrEqualTo(0.80, improvement);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task MeasureLegacyAsync(IPage page)
    {
        var anchors = page.Locator("a[href]");
        for (var index = 0; index < await anchors.CountAsync(); index++)
        {
            var anchor = anchors.Nth(index);
            _ = await anchor.GetAttributeAsync("href");
            _ = await anchor.InnerTextAsync();
            _ = await anchor.GetAttributeAsync("aria-label");
            _ = await anchor.GetAttributeAsync("title");
            _ = await anchor.IsVisibleAsync();
            _ = await anchor.EvaluateAsync<string>("e => e.closest('header,footer') ? 'edge' : 'content'");
        }
    }

    private static string WriteFixture(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"playwright-efficiency-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, $"<!doctype html><html><body>{body}</body></html>");
        return path;
    }
}

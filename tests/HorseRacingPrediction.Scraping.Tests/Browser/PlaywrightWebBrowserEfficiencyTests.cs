using System.Diagnostics;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Pages;
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

            Assert.IsLessThan(TimeSpan.FromSeconds(4), elapsed);
            Assert.Contains("Ready", JraSnapshotView.Create(snapshot).Headings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task NavigateForSnapshotAsync_CalendarWaitsForVisibleRacecourseBeyondThreeSeconds()
    {
        var path = WriteFixture(CalendarShell("""
            setTimeout(() => renderCalendar('2026年9月', '5 中山'), 3250);
            """));
        try
        {
            var snapshotter = new CountingSnapshotter();
            await using var browser = await PlaywrightWebBrowser.CreateForTestingAsync(
                TimeSpan.FromSeconds(5),
                _ => true,
                snapshotter);
            var startedAt = Stopwatch.GetTimestamp();

            await browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri);
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var snapshot = await browser.CapturePageSnapshotAsync();
            var calendar = (JraCalendarPage)new CalendarPageParser().Parse(snapshot);

            Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(3), elapsed);
            Assert.IsLessThan(TimeSpan.FromSeconds(5), elapsed);
            Assert.AreEqual(new(2026, 9), calendar.Month);
            Assert.AreEqual(new DateOnly(2026, 9, 5), calendar.RaceDates.Single().Date);
            Assert.AreEqual(1, snapshotter.CaptureCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task NavigateForSnapshotAsync_CalendarRetriesOneIncompleteGetAndCapturesOnce()
    {
        var path = WriteFixture(CalendarShell("""
            const attempt = Number(sessionStorage.getItem('calendar-attempt') || '0') + 1;
            sessionStorage.setItem('calendar-attempt', String(attempt));
            document.getElementById('attempt').textContent = String(attempt);
            if (attempt === 2) renderCalendar('2026年9月', '5 中山');
            """));
        try
        {
            var snapshotter = new CountingSnapshotter();
            await using var browser = await PlaywrightWebBrowser.CreateForTestingAsync(
                TimeSpan.FromMilliseconds(200),
                _ => true,
                snapshotter);

            await browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri);
            var snapshot = await browser.CapturePageSnapshotAsync();
            var view = JraSnapshotView.Create(snapshot);

            StringAssert.Contains(view.MainText, "2");
            _ = (JraCalendarPage)new CalendarPageParser().Parse(snapshot);
            Assert.AreEqual(1, snapshotter.CaptureCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task NavigateForSnapshotAsync_CalendarTimeoutDoesNotCaptureIncompletePage()
    {
        var path = WriteFixture(CalendarShell(string.Empty));
        try
        {
            var snapshotter = new CountingSnapshotter();
            await using var browser = await PlaywrightWebBrowser.CreateForTestingAsync(
                TimeSpan.FromMilliseconds(100),
                _ => true,
                snapshotter);
            var startedAt = Stopwatch.GetTimestamp();

            var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri));

            Assert.IsLessThan(TimeSpan.FromSeconds(5), Stopwatch.GetElapsedTime(startedAt));
            StringAssert.Contains(exception.Message, "Missing=YearMonthOrDateOrRacecourse");
            Assert.AreEqual(0, snapshotter.CaptureCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task NavigateForSnapshotAsync_CalendarReadinessHonorsCancellation()
    {
        var path = WriteFixture(CalendarShell(string.Empty));
        try
        {
            await using var browser = await PlaywrightWebBrowser.CreateForTestingAsync(
                TimeSpan.FromSeconds(5),
                _ => true);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var startedAt = Stopwatch.GetTimestamp();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri, cancellation.Token));

            Assert.IsLessThan(TimeSpan.FromSeconds(2), Stopwatch.GetElapsedTime(startedAt));
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
    public async Task SetFieldValueForSnapshotAsync_WaitsForDelayedVisibleField()
    {
        var path = WriteFixture("""
            <main><h1>競走馬検索</h1><form id='search'></form></main>
            <script>
              setTimeout(() => {
                const field = document.createElement('input');
                field.name = 'iv_h_name';
                document.getElementById('search').appendChild(field);
              }, 200);
            </script>
            """);
        try
        {
            var snapshotter = new CountingSnapshotter();
            await using var browser = await PlaywrightWebBrowser.CreateForTestingAsync(
                TimeSpan.FromSeconds(1),
                _ => false,
                snapshotter,
                TimeSpan.FromSeconds(2));
            await browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri);

            await browser.SetFieldValueForSnapshotAsync("iv_h_name", "テストホース");
            var forms = await browser.GetFormsAsync();

            Assert.AreEqual("テストホース", forms.Single().Fields.Single().Value);
            Assert.AreEqual(0, snapshotter.CaptureCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SetFieldValueForSnapshotAsync_VisibleFieldUsesImmediateFastPath()
    {
        var path = WriteFixture("<main><form><input name='iv_h_name'></form></main>");
        try
        {
            var snapshotter = new CountingSnapshotter();
            await using var browser = await PlaywrightWebBrowser.CreateForTestingAsync(
                TimeSpan.FromSeconds(1),
                _ => false,
                snapshotter,
                TimeSpan.FromMilliseconds(1));
            await browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri);

            await browser.SetFieldValueForSnapshotAsync("iv_h_name", "テストホース");

            Assert.AreEqual(0, snapshotter.CaptureCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SetFieldValueForSnapshotAsync_MissingFieldRemainsStructuralFailure()
    {
        var path = WriteFixture("<main><h1>競走馬検索</h1><form></form></main>");
        try
        {
            await using var browser = await PlaywrightWebBrowser.CreateForTestingAsync(
                TimeSpan.FromSeconds(1),
                _ => false,
                fieldReadinessTimeout: TimeSpan.FromMilliseconds(100));
            var url = new Uri(path).AbsoluteUri;
            await browser.NavigateForSnapshotAsync(url);

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => browser.SetFieldValueForSnapshotAsync("iv_h_name", "テストホース"));

            StringAssert.Contains(exception.Message, "iv_h_name");
            StringAssert.Contains(exception.Message, url);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SetFieldValueForSnapshotAsync_ReadinessWaitHonorsCancellation()
    {
        var path = WriteFixture("<main><h1>競走馬検索</h1><form></form></main>");
        try
        {
            await using var browser = await PlaywrightWebBrowser.CreateForTestingAsync(
                TimeSpan.FromSeconds(1),
                _ => false,
                fieldReadinessTimeout: TimeSpan.FromSeconds(5));
            await browser.NavigateForSnapshotAsync(new Uri(path).AbsoluteUri);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var startedAt = Stopwatch.GetTimestamp();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => browser.SetFieldValueForSnapshotAsync("iv_h_name", "テストホース", cancellation.Token));

            Assert.IsLessThan(TimeSpan.FromSeconds(2), Stopwatch.GetElapsedTime(startedAt));
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

    private static string CalendarShell(string script) => $$"""
        <main>
          <h2 id="heading">開催日程</h2>
          <p id="attempt"></p>
          <div id="cal_unit"><table class="rc_table"><caption id="caption"></caption><tbody><tr><td id="day"></td></tr></tbody></table></div>
        </main>
        <script>
          function renderCalendar(month, day) {
            document.getElementById('heading').textContent = `開催日程 ${month}`;
            document.getElementById('caption').textContent = month;
            document.getElementById('day').textContent = day;
          }
          {{script}}
        </script>
        """;

    private sealed class CountingSnapshotter : IPageSnapshotter
    {
        private readonly PlaywrightPageSnapshotter _inner = new();

        public int CaptureCount { get; private set; }

        public async Task<PageSnapshot> CaptureAsync(
            IPage page,
            PageSnapshotOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return await _inner.CaptureAsync(page, options, cancellationToken);
        }
    }
}

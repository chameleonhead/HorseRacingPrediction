using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Browser.Snapshots;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public sealed class PlaywrightWebBrowserSnapshotTests
{
    [TestMethod]
    [TestCategory("External")]
    public async Task GetPageSnapshotAsync_TableCellWithClasses_ReturnsDomFragments()
    {
        var logger = new CapturingLogger<PlaywrightWebBrowser>();
        await using var browser = await PlaywrightWebBrowser.CreateAsync(logger: logger);
        const string html = "<table><tr><th>馬名</th></tr><tr><td><a href='/race/1'>1R</a><p class='owner'>藤田 晋</p><div class='cell weight'>488kg<span class='transition'>(-2)</span></div></td></tr></table>";
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var responseTask = ServeHtmlOnceAsync(listener, html);
        await browser.NavigateAsync($"http://127.0.0.1:{port}/");
        await responseTask;

        var snapshot = await browser.GetPageSnapshotAsync();

        var cell = snapshot.Tables.SelectMany(table => table.Rows)
            .SelectMany(row => row.Cells)
            .First(cell => cell.Fragments.Any(fragment => fragment.ClassTokens.Contains("owner")));
        Assert.AreEqual("藤田 晋", FindByClass(cell, "owner")?.Text);
        Assert.AreEqual("488kg(-2)", FindByClass(cell, "weight")?.Text);
        Assert.AreEqual("(-2)", FindByClass(cell, "transition")?.Text);
        Assert.AreEqual("/race/1", cell.Fragments.Single(fragment => fragment.TagName == "a").RawUrl);
        Assert.IsTrue(logger.Messages.Any(message => message.Contains("Fragments=4", StringComparison.Ordinal)));
    }

    private static async Task ServeHtmlOnceAsync(TcpListener listener, string html)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }

        var body = Encoding.UTF8.GetBytes(html);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(body);
    }

    [TestMethod]
    public void DefaultLaunchOptions_UsePlatformCompatibleChromiumArguments()
    {
        var options = PlaywrightWebBrowser.CreateDefaultLaunchOptions();

        Assert.IsTrue(options.Headless);
        Assert.IsFalse(options.ChromiumSandbox);
        var arguments = options.Args!.ToArray();
        CollectionAssert.Contains(arguments, "--disable-dev-shm-usage");
        CollectionAssert.Contains(arguments, "--no-zygote");
        if (OperatingSystem.IsWindows())
        {
            CollectionAssert.DoesNotContain(arguments, "--single-process");
        }
        else
        {
            CollectionAssert.Contains(arguments, "--single-process");
        }
    }

    private static PageElementFragmentSnapshot? FindByClass(PageTableCellSnapshot cell, string className)
        => cell.Fragments.FirstOrDefault(fragment => fragment.ClassTokens.Contains(className));

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}

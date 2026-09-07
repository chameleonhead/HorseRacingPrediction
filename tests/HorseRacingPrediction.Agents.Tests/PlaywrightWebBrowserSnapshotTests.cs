using HorseRacingPrediction.Scraping.Browser;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public sealed class PlaywrightWebBrowserSnapshotTests
{
    [TestMethod]
    [TestCategory("External")]
    public async Task GetPageSnapshotAsync_TableCellWithClasses_ReturnsDomFragments()
    {
        await using var browser = await PlaywrightWebBrowser.CreateAsync();
        const string html = "<table><tr><th>馬名</th></tr><tr><td><a href='/race/1'>1R</a><p class='owner'>藤田 晋</p><div class='cell weight'>488kg<span class='transition'>(-2)</span></div></td></tr></table>";
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var responseTask = ServeHtmlOnceAsync(listener, html);
        await browser.NavigateAsync($"http://127.0.0.1:{port}/");
        await responseTask;

        var snapshot = await browser.GetPageSnapshotAsync();

        var cell = snapshot.Sections.SelectMany(section => section.Tables)
            .SelectMany(table => table.Cells ?? [])
            .SelectMany(row => row)
            .First(cell => cell.FindByClass("owner") is not null);
        Assert.AreEqual("藤田 晋", cell.FindByClass("owner")?.Text);
        Assert.AreEqual("488kg(-2)", cell.FindByClass("weight")?.Text);
        Assert.AreEqual("(-2)", cell.FindByClass("transition")?.Text);
        Assert.AreEqual("/race/1", cell.Fragments.Single(fragment => fragment.TagName == "a").Href);
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

    [TestMethod]
    public void IsTextCoveredByExistingSections_ReturnsTrueForAlreadyCapturedBlock()
    {
        var sections = CreateSections("3歳以上1勝クラス コース：1,700 メートル（ダート・右）");

        var covered = PlaywrightWebBrowser.IsTextCoveredByExistingSections(
            "コース：1,700 メートル（ダート・右）",
            sections);

        Assert.IsTrue(covered);
    }

    [TestMethod]
    public void IsTextCoveredByExistingSections_ReturnsFalseForUncapturedCourseBlock()
    {
        var sections = CreateSections("着順 馬番 馬名 騎手 タイム");

        var covered = PlaywrightWebBrowser.IsTextCoveredByExistingSections(
            "コース：1,700 メートル（ダート・右）",
            sections);

        Assert.IsFalse(covered);
    }

    private static IReadOnlyList<PageSectionSnapshot> CreateSections(string mainText)
        =>
        [
            new PageSectionSnapshot(
                title: "Result",
                mainText,
                headings: [],
                links: [],
                actions: [],
                tables: [])
        ];
}

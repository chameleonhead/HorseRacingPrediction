using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class JraPageReaderTests
{
    private sealed record FakePage(JraPageKind Kind, string Url) : IJraPage;

    private sealed class StubParser(JraPageKind kind, int priority, bool canParse) : IJraPageParser
    {
        public JraPageKind Kind => kind;

        public int Priority => priority;

        public bool CanParse(PageSnapshot snapshot) => canParse;

        public IJraPage Parse(PageSnapshot snapshot)
            => new FakePage(kind, snapshot.Url);
    }

    [TestMethod]
    public async Task ReadAsync_MultipleParsersMatch_HigherPriorityWins()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(string.Empty, new PageSnapshot(string.Empty, "title", []));

        var low = new StubParser(JraPageKind.RaceList, priority: 10, canParse: true);
        var high = new StubParser(JraPageKind.Calendar, priority: 100, canParse: true);

        var reader = new JraPageReader(browser, [low, high]);

        var page = await reader.ReadAsync();

        Assert.AreEqual(JraPageKind.Calendar, page.Kind);
    }

    [TestMethod]
    public async Task ReadAsync_NoParserMatches_ReturnsUnknownPage()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(string.Empty, new PageSnapshot(string.Empty, "title", []));

        var parser = new StubParser(JraPageKind.Calendar, priority: 100, canParse: false);

        var reader = new JraPageReader(browser, [parser]);

        var page = await reader.ReadAsync();

        Assert.AreEqual(JraPageKind.Unknown, page.Kind);
        Assert.IsInstanceOfType<JraUnknownPage>(page);
    }
}

using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Navigation;

[TestClass]
public sealed class JraRetiredDirectoryTests
{
    [TestMethod]
    [DataRow("Jockey", "引退騎手", "https://www.jra.go.jp/datafile/meikan/jretirement.html")]
    [DataRow("Trainer", "引退調教師", "https://www.jra.go.jp/datafile/meikan/retirement.html")]
    public async Task ToSubjectProfileAsync_UsesActualRetiredDirectoryLabel(
        string subjectType, string label, string retiredUrl)
    {
        var directoryUrl = subjectType == "Jockey"
            ? "https://www.jra.go.jp/datafile/meikan/jockey.html"
            : "https://www.jra.go.jp/datafile/meikan/trainer.html";
        var browser = new FakeWebBrowser();
        browser.SetLinks(directoryUrl, new[] { new PageLinkSnapshot(retiredUrl, label) });
        browser.SetLinks(retiredUrl, Array.Empty<PageLinkSnapshot>());

        var navigator = new JraNavigator(browser, new JraPageReader(browser, []));

        await Assert.ThrowsExactlyAsync<JraSubjectIdentificationException>(() =>
            navigator.ToSubjectProfileAsync(new JraSubjectIdentity(subjectType, "未掲載の対象")));

        CollectionAssert.Contains(browser.NavigatedUrls, retiredUrl);
        Assert.AreEqual(retiredUrl, browser.CurrentUrl);
    }

    [TestMethod]
    public void SelectUniqueJraLink_DuplicateSameUrl_IsAllowed()
    {
        var first = new PageLinkSnapshot(
            "https://www.jra.go.jp/datafile/meikan/jretirement.html", "引退騎手");
        var second = new PageLinkSnapshot(
            "https://www.jra.go.jp/datafile/meikan/jretirement.html", "引退騎手");

        Assert.AreSame(first, JraNavigator.SelectUniqueJraLink([first, second], "引退騎手"));
    }

    [TestMethod]
    public void SelectUniqueJraLink_ConflictingUrls_Throws()
    {
        var links = new[]
        {
            new PageLinkSnapshot("https://www.jra.go.jp/datafile/meikan/a.html", "引退調教師"),
            new PageLinkSnapshot("https://www.jra.go.jp/datafile/meikan/b.html", "引退調教師"),
        };

        Assert.ThrowsExactly<JraCollectionException>(() =>
            JraNavigator.SelectUniqueJraLink(links, "引退調教師"));
    }

    [TestMethod]
    [DataRow("http://www.jra.go.jp/datafile/meikan/retirement.html")]
    [DataRow("https://evil.example/retirement.html")]
    [DataRow("javascript:void(0)")]
    public void SelectUniqueJraLink_UnsafeUrl_Throws(string url)
    {
        var links = new[] { new PageLinkSnapshot(url, "引退調教師") };

        Assert.ThrowsExactly<JraCollectionException>(() =>
            JraNavigator.SelectUniqueJraLink(links, "引退調教師"));
    }

    [TestMethod]
    public void SelectUniqueJraLink_Missing_ReturnsNull()
        => Assert.IsNull(JraNavigator.SelectUniqueJraLink([], "引退騎手"));
}

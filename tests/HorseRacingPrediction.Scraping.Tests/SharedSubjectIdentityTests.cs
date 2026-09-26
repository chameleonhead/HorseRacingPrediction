using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests;

[TestClass]
public sealed class SharedSubjectIdentityTests
{
    [TestMethod]
    public async Task Navigator_HorseSearchUsesHorseIdentityNameNormalization()
    {
        const string searchFormUrl = "https://www.jra.go.jp/JRADB/accessO.html";
        const string searchResultUrl = "https://www.jra.go.jp/JRADB/search/horse";
        const string profileUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024102539/FC";
        var browser = new FakeWebBrowser();
        browser.SetClickDestination("競走馬検索", searchFormUrl);
        browser.SetSubmitDestination(searchResultUrl);
        browser.SetSnapshot(searchResultUrl, new TestPageSnapshot(searchResultUrl, "競走馬検索", [new(
            "検索結果", string.Empty, [new(profileUrl, "マル外 サンプル")], [], [], ["競走馬検索"])]));
        browser.SetSnapshot(profileUrl, HorseProfile(profileUrl, "マル外 サンプル"));

        var navigator = new JraNavigator(browser, Reader(browser));

        var page = await navigator.ToSubjectProfileAsync(new("Horse", "サンプル"));

        Assert.AreEqual(profileUrl, page.Profile.SourceIdentity);
        Assert.AreEqual(1, browser.ClickedTexts.Count(text => text == "マル外 サンプル"));
    }

    [TestMethod]
    public void Parser_HorseSourceIdentityIgnoresExtraQueryButRejectsDifferentCname()
    {
        const string canonical = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024102539/FC";
        var page = SubjectProfilePageParser.Parse(HorseProfile(canonical, "サンプル"), "Horse");

        SubjectProfilePageParser.Validate(page, new("Horse", "サンプル",
            SourceIdentity: canonical + "&utm=test"));

        var mismatch = Assert.ThrowsExactly<JraSubjectIdentificationException>(() =>
            SubjectProfilePageParser.Validate(page, new("Horse", "サンプル",
                SourceIdentity: "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024102539/OTHER")));
        Assert.AreEqual(JraSubjectIdentificationFailureKind.SourceIdentityMismatch, mismatch.Kind);
    }

    [TestMethod]
    public void Parser_HorseSourceIdentityRejectsUnsafeHost()
    {
        const string profileUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002024102539/FC";
        var page = SubjectProfilePageParser.Parse(HorseProfile(profileUrl, "サンプル"), "Horse");

        var mismatch = Assert.ThrowsExactly<JraSubjectIdentificationException>(() =>
            SubjectProfilePageParser.Validate(page, new("Horse", "サンプル",
                SourceIdentity: "https://evil.example/JRADB/accessU.html?CNAME=pw01dud002024102539/FC")));
        Assert.AreEqual(JraSubjectIdentificationFailureKind.SourceIdentityMismatch, mismatch.Kind);
    }

    private static JraPageReader Reader(FakeWebBrowser browser)
        => new(browser, [new CalendarPageParser(), new RaceListPageParser(), new RaceCardPageParser(), new RaceResultPageParser()]);

    private static TestPageSnapshot HorseProfile(string url, string name)
        => new(url, "競走馬情報", [new(
            "競走馬情報", string.Empty, [], [],
            [new(["項目", "値"], [["生年月日", "2024年4月11日"]])],
            [$"競走馬情報 {name}"])]);
}

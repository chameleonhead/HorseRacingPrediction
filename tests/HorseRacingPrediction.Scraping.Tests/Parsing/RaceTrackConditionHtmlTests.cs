using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceTrackConditionHtmlTests
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [TestMethod]
    [DataRow("<li><span>ダート</span><span>重</span></li>", "ダート:重")]
    [DataRow("<li>芝 良</li>", "芝:良")]
    [DataRow("<li>芝 稍重</li><li>ダート 不良</li>", "芝:稍重 ダート:不良")]
    [DataRow("<li><span>ダート</span>\n <span> 重 </span></li>", "ダート:重")]
    public async Task OverviewOnly_IgnoresOwnerAndHorseNames(string tracks, string expected)
    {
        var snapshot = await Capture(tracks);
        var overview = snapshot.FindByKind(PageContentKind.List)
            .Single(node => node.Source?.AncestorClassTokens?.Contains("baba") == true);
        Assert.IsTrue(overview.Source!.AncestorClassTokens!.Contains("race_header"));
        var parsed = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);
        Assert.AreEqual(expected, parsed.TrackConditionText);
        Assert.AreEqual("雨", parsed.WeatherText);
    }

    [TestMethod]
    public async Task DedicatedUnknownValue_IsNotSilentlyIgnored()
    {
        var snapshot = await Capture("<li>芝 極重</li>");
        var error = Assert.ThrowsExactly<JraUnexpectedValueException>(() => new RaceResultPageParser().Parse(snapshot));
        Assert.AreEqual("TrackCondition(Turf)", error.FieldName);
        Assert.AreEqual("極重", error.RawValue);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("<li>芝 良</li><li>芝 重</li>")]
    [DataRow("<li>芝 良</li><li>芝 良</li>")]
    [DataRow("芝 極重<li>ダート 重</li>")]
    public async Task MissingOrDuplicateDedicatedCondition_IsRejected(string tracks)
    {
        var snapshot = await Capture(tracks);
        Assert.ThrowsExactly<JraPageStructureException>(() => new RaceResultPageParser().Parse(snapshot));
    }

    [TestMethod]
    public async Task LookalikeListOutsideOverview_IsNotFallback()
    {
        var snapshot = await Capture("<li>ダート 重</li>", "unrelated");
        Assert.ThrowsExactly<JraPageStructureException>(() => new RaceResultPageParser().Parse(snapshot));
    }

    [TestMethod]
    public async Task UnknownDedicatedWeather_CannotBeHiddenByEarlierValidWeather()
    {
        var snapshot = await Capture("<li>ダート 重</li>", weather: "不明");
        var error = Assert.ThrowsExactly<JraUnexpectedValueException>(() => new RaceResultPageParser().Parse(snapshot));
        Assert.AreEqual("Weather", error.FieldName);
    }

    [TestMethod]
    public async Task OtherRaceCancellationNotice_DoesNotSuppressRequiredOverview()
    {
        var snapshot = await Capture("<li>ダート 重</li>", notice: "第11競走は取り止めとなりました");
        var parsed = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);
        Assert.IsFalse(parsed.IsOfficiallyCancelled);
        Assert.AreEqual("雨", parsed.WeatherText);
        Assert.AreEqual("ダート:重", parsed.TrackConditionText);
        var missing = await Capture("", notice: "第11競走は取り止めとなりました");
        Assert.ThrowsExactly<JraPageStructureException>(() => new RaceResultPageParser().Parse(missing));
    }

    [TestMethod]
    [DataRow(10, true)]
    [DataRow(11, false)]
    public async Task CancellationWithoutResultTable_MustNameTargetRace(int cancelledRace, bool expected)
    {
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync($"<main><h1>2026年4月4日 中山 10レース 結果</h1><p>第{cancelledRace}競走は取り止めとなりました</p></main>");
        var snapshot = await new PlaywrightPageSnapshotter().CaptureAsync(page);
        var parser = new RaceResultPageParser();
        Assert.AreEqual(expected, parser.CanParse(snapshot));
        if (expected)
            Assert.IsTrue(((JraRaceResultPage)parser.Parse(snapshot)).IsOfficiallyCancelled);
        else
            Assert.ThrowsExactly<JraPageParseException>(() => parser.Parse(snapshot));
    }

    [TestMethod]
    [DataRow("阪神第10競走は取り止めとなりました")]
    [DataRow("2026年4月3日 中山第10競走は取り止めとなりました")]
    [DataRow("この競走は取り止めとなりました")]
    public async Task UnscopedCancellationAnnouncement_IsNotEvidence(string announcement)
    {
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync($"<main><h1>2026年4月4日 中山 10レース 結果</h1><p>{announcement}</p></main>");
        var snapshot = await new PlaywrightPageSnapshotter().CaptureAsync(page);
        var parser = new RaceResultPageParser();
        Assert.IsFalse(parser.CanParse(snapshot));
        Assert.ThrowsExactly<JraPageParseException>(() => parser.Parse(snapshot));
    }

    private async Task<PageSnapshot> Capture(string tracks, string headerClass = "race_header", string weather = "雨", string notice = "")
    {
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync($$"""
            <main>
              <h1>レース結果2026年4月4日（土曜）3回中山3日 10レース</h1>
              <p>馬主：青芝商事(株) 芝良商会 ダート良牧場</p>
              <p>{{notice}}</p>
              <ul><li>天候 晴</li><li>芝 良</li></ul>
              <div class="{{headerClass}}"><h2>千葉日報杯</h2><div class="cell baba">
                <ul><li class="weather"><span class="cap">天候</span><span class="txt">{{weather}}</span></li>{{tracks}}</ul>
              </div></div>
              <table><tr><th>着順</th><th>馬番</th><th>馬名</th><th>騎手</th><th>タイム</th></tr>
                <tr><td>1</td><td>3</td><td>芝良テスト</td><td>騎手</td><td>1:53.4</td></tr></table>
              <table><tr><th>式別</th><th>組合せ</th><th>払戻金</th></tr>
                <tr><td>単勝</td><td>3</td><td>610円</td></tr></table>
              <div><span class="win_owner">馬主：青芝商事(株)</span></div>
              <ul><li>天候 晴</li><li>芝 良</li></ul>
            </main>
            """);
        return await new PlaywrightPageSnapshotter().CaptureAsync(page);
    }

    [TestMethod]
    [TestCategory("External")]
    public async Task ProductionIncidentPage_ParsesDirtHeavyWithoutInventingTurf()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202603031020260404/0B",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var snapshot = await new PlaywrightPageSnapshotter().CaptureAsync(page);
        var result = (JraRaceResultPage)new RaceResultPageParser().Parse(snapshot);
        Assert.AreEqual("ダート:重", result.TrackConditionText);
        Assert.AreEqual(15, result.Results.Count);
    }
}

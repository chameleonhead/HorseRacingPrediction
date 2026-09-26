using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceCardCancellationTests
{
    private const string Url = "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde2026092603/00";
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
    [DataRow("取消", RaceEntryParticipationStatus.Cancelled)]
    [DataRow("除外", RaceEntryParticipationStatus.Excluded)]
    public async Task ParseHtml_NonActiveRowRetainsRowIdentityAndAttributesWithoutPastNumberInference(
        string statusText, RaceEntryParticipationStatus expectedStatus)
    {
        var rows = string.Concat(Enumerable.Range(1, 16).Select(number => number == 6
            ? Row("3", statusText, "ニシノドリーマー", "000006", pastText: "過去成績 11番", owner: "取消を含む馬主")
            : Row(number.ToString(), number.ToString(), $"通常馬{number}", $"{number:000000}")));
        var snapshot = await CaptureHtmlAsync(rows);

        var page = Parse(snapshot);
        var cancelled = page.Entries.Single(x => x.HorseName == "ニシノドリーマー");

        Assert.HasCount(16, page.Entries);
        Assert.HasCount(15, page.Entries.Where(x => x.ParticipationStatus == RaceEntryParticipationStatus.Active));
        Assert.IsNull(cancelled.HorseNumber);
        Assert.AreEqual(expectedStatus, cancelled.ParticipationStatus);
        Assert.AreEqual("取消を含む馬主", cancelled.OwnerName);
        Assert.AreEqual("調教師", cancelled.TrainerName);
        Assert.AreEqual(HorseSource("000006"), cancelled.HorseSourceIdentity);
    }

    [TestMethod]
    public async Task ParseHtml_CancellationTextOutsideNumberCellDoesNotChangeActiveStatus()
    {
        var snapshot = await CaptureHtmlAsync(Row("6", "6", "取消候補という馬名", "000007",
            owner: "除外を含む馬主", pastText: "過去成績 取消"));

        var entry = Parse(snapshot).Entries.Single();

        Assert.AreEqual(6, entry.HorseNumber);
        Assert.AreEqual(RaceEntryParticipationStatus.Active, entry.ParticipationStatus);
    }

    [TestMethod]
    public async Task ParseHtml_EmptyNumberRemainsProvisionalActive()
    {
        var snapshot = await CaptureHtmlAsync(Row("", "", "未確定馬", "000008"));

        var entry = Parse(snapshot).Entries.Single();

        Assert.IsNull(entry.HorseNumber);
        Assert.AreEqual(RaceEntryParticipationStatus.Active, entry.ParticipationStatus);
    }

    [TestMethod]
    public async Task ParseHtml_UnknownNumberTextStillThrows()
    {
        var snapshot = await CaptureHtmlAsync(Row("1", "馬番?", "不正馬", "000009"));

        var exception = Assert.ThrowsExactly<JraValueParseException>(() => Parse(snapshot));

        Assert.AreEqual("HorseNumber", exception.FieldName);
        Assert.AreEqual("馬番?", exception.RawValue);
    }

    [TestMethod]
    public async Task ParseHtml_NonActiveRowWithoutHorseSourceIdentityThrows()
    {
        var snapshot = await CaptureHtmlAsync(Row("3", "除外", "識別不能馬", null));

        var exception = Assert.ThrowsExactly<JraHorseSourceIdentityUnavailableException>(() => Parse(snapshot));

        Assert.AreEqual(new DateOnly(2026, 9, 26), exception.RaceId.Date);
    }

    private async Task<PageSnapshot> CaptureHtmlAsync(string rows)
    {
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync($"""
            <main>
              <h1>2026年9月26日（土曜）3回中山3日 3レース</h1>
              <h2>HTMLテストステークス</h2>
              <p>発走 11:20</p>
              <table>
                <thead><tr><th>枠番</th><th>馬番</th><th>馬名</th><th>性齢/毛色<br>負担重量<br>騎手名</th></tr></thead>
                <tbody>{rows}</tbody>
              </table>
            </main>
            """);
        return await new PlaywrightPageSnapshotter().CaptureAsync(page);
    }

    private static string Row(string frame, string horseNumber, string horseName, string? sourceSuffix,
        string pastText = "", string owner = "馬主 HTML")
    {
        var source = sourceSuffix is null ? horseName : $"<a href=\"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026{sourceSuffix}/00\">{horseName}</a>";
        return $"""
            <tr>
              <td>{frame}</td>
              <td>{horseNumber}</td>
              <td><div class="name">{source}</div><div class="past">{pastText}</div><p class="owner">{owner}</p><div class="trainer">調教師（栗東）</div></td>
              <td>牡3/栗<br>57.0kg<br>騎手</td>
            </tr>
            """;
    }

    private static JraRaceCardPage Parse(PageSnapshot snapshot)
        => (JraRaceCardPage)new RaceCardPageParser().Parse(snapshot);

    private static string HorseSource(string suffix)
        => $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026{suffix}/00";
}

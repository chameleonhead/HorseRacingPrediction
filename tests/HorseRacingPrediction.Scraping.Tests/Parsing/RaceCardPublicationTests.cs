using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceCardPublicationTests
{
    private const string Url = "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde2026092603/00";
    private static readonly DateOnly Date = new(2026, 9, 26);
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
    public async Task ParseHtml_AllHorseAndFrameCellsBlank_RetainsIdentifiedEntries()
    {
        var snapshot = await CaptureHtmlAsync(HtmlRow("", "", "HTML確定待ち馬A", "000101")
            + HtmlRow("", "", "HTML確定待ち馬B", "000102"));

        var page = Parse(snapshot);
        Assert.AreEqual(new RaceId(Date, RaceCourse.Nakayama, 3), page.RaceId);
        Assert.HasCount(2, page.Entries);
        Assert.IsTrue(page.Entries.All(entry => entry.HorseNumber is null && entry.FrameNumber is null));
        Assert.IsTrue(page.Entries.All(entry => entry.HorseSourceIdentity is not null));
    }

    [TestMethod]
    public async Task ParseHtml_ConfirmedIconAndTextNumbers_PreservesOfficialNumbersNotRowPositions()
    {
        var snapshot = await CaptureHtmlAsync(
            HtmlRow("", "馬番2", "HTML公式馬A", "000103", numberAsAlt: true)
            + HtmlRow("1", "1", "HTML公式馬B", "000104"));

        var page = Parse(snapshot);

        Assert.AreEqual(2, page.Entries[0].HorseNumber);
        Assert.AreEqual(1, page.Entries[1].HorseNumber);
        Assert.AreEqual("HTML公式馬A", page.Entries[0].HorseName);
        Assert.AreEqual("馬主 HTML", page.Entries[0].OwnerName);
    }

    [TestMethod]
    public async Task ParseHtml_MixedBlankAndConfirmedNumber_RetainsKnownAndUnknown()
    {
        var snapshot = await CaptureHtmlAsync(
            HtmlRow("1", "1", "HTML部分馬A", "000105")
            + HtmlRow("", "", "HTML部分馬B", "000106"));

        var page = Parse(snapshot);
        Assert.AreEqual(1, page.Entries[0].HorseNumber);
        Assert.IsNull(page.Entries[1].HorseNumber);
    }

    [TestMethod]
    public async Task ParseHtml_SelectedRaceGradeWinsOverUnrelatedHeadingAndImage()
    {
        var snapshot = await CaptureHtmlAsync(
            HtmlRow("1", "1", "HTMLグレード馬", "000107"),
            selectedRaceHeading: "3レース 選択レース <img alt=\"GIII\">",
            unrelatedHeading: "過去レース GII <img alt=\"GII\">");

        var page = Parse(snapshot);

        Assert.AreEqual("選択レース GIII", page.RaceName);
        Assert.AreEqual("G3", page.GradeCode);
    }

    [TestMethod]
    public void Parse_AllHorseAndFrameNumbersBlank_RetainsIdentifiedEntries()
    {
        var page = Parse(Card(("", "", "確定待ち馬A", null), ("", "", "確定待ち馬B", null)));
        Assert.AreEqual(new RaceId(Date, RaceCourse.Nakayama, 3), page.RaceId);
        Assert.HasCount(2, page.Entries);
        Assert.IsTrue(page.Entries.All(entry => entry.HorseNumber is null && entry.FrameNumber is null));
        Assert.IsTrue(page.Entries.All(entry => entry.HorseSourceIdentity is not null));
    }

    [TestMethod]
    public void Parse_RearrangedConfirmedNumbers_PreservesOfficialNumbersNotRowPositions()
    {
        var page = Parse(Card(("2", "2", "公式馬A", null), ("1", "1", "公式馬B", null)));

        Assert.HasCount(2, page.Entries);
        Assert.AreEqual("公式馬A", page.Entries[0].HorseName);
        Assert.AreEqual(2, page.Entries[0].HorseNumber);
        Assert.AreEqual("公式馬B", page.Entries[1].HorseName);
        Assert.AreEqual(1, page.Entries[1].HorseNumber);
    }

    [TestMethod]
    public void Parse_MixedBlankAndConfirmedNumber_RetainsKnownAndUnknown()
    {
        var page = Parse(Card(("1", "1", "部分確定馬A", null), ("", "", "部分確定馬B", null)));
        Assert.AreEqual(1, page.Entries[0].HorseNumber);
        Assert.IsNull(page.Entries[1].HorseNumber);
    }

    [TestMethod]
    public void Parse_UnconfirmedNumberWithoutSourceIdentity_WaitsWithoutSaving()
    {
        var exception = Assert.ThrowsExactly<JraHorseSourceIdentityUnavailableException>(
            () => Parse(Card(("", "", "識別不能馬A", ""), ("", "", "識別可能馬B", null))));
        Assert.AreEqual(new RaceId(Date, RaceCourse.Nakayama, 3), exception.RaceId);
    }

    [TestMethod]
    public void Parse_DuplicateHorseNumbers_ThrowsConsistencyException()
    {
        var exception = Assert.ThrowsExactly<JraResultConsistencyException>(
            () => Parse(Card(("1", "1", "重複馬A", null), ("1", "1", "重複馬B", null))));

        Assert.AreEqual("HorseNumber", exception.FieldName);
    }

    [TestMethod]
    public void Parse_GarbageHorseNumber_ThrowsValueParseException()
    {
        var exception = Assert.ThrowsExactly<JraValueParseException>(
            () => Parse(Card(("1", "馬番?", "不正馬", null), ("2", "2", "正常馬", null))));

        Assert.AreEqual("HorseNumber", exception.FieldName);
        Assert.AreEqual("馬番?", exception.RawValue);
    }

    [TestMethod]
    public void Parse_HorseNumberIconAccessibleName_ResolvesOfficialNumberAndHorseIdentity()
    {
        var page = Parse(Card(
            ("", "", "アイコン馬", HorseSource("000001")),
            ("1", "1", "通常馬", HorseSource("000002")), numberAccessibleName: "馬番2"));

        Assert.AreEqual(2, page.Entries[0].HorseNumber);
        Assert.AreEqual(1, page.Entries[1].HorseNumber);
        Assert.AreEqual(HorseSource("000001"), page.Entries[0].HorseSourceIdentity);
        Assert.AreEqual("馬主 アイコン", page.Entries[0].OwnerName);
    }

    [TestMethod]
    public void Parse_DuplicateHeaderRow_IsIgnoredBeforePublicationValidation()
    {
        var page = Parse(Card(("1", "1", "ヘッダー重複馬A", null), ("2", "2", "ヘッダー重複馬B", null), includeHeaderRow: true));

        Assert.HasCount(2, page.Entries);
        CollectionAssert.AreEqual(new[] { 1, 2 }, page.Entries.Select(x => x.HorseNumber).ToArray());
    }

    [TestMethod]
    public async Task Parse_HtmlHorseNumberWithBlinkerIcon_PreservesOfficialNumber()
    {
        var snapshot = await CaptureHtmlAsync(HtmlRow("2", "3<span class=\"horse_icon blinker\"><img alt=\"ブリンカー着用\"></span>", "装具表示馬", "000001"));
        Assert.AreEqual(3, Parse(snapshot).Entries.Single().HorseNumber);
    }

    private static JraRaceCardPage Parse(PageSnapshot snapshot)
        => (JraRaceCardPage)new RaceCardPageParser().Parse(snapshot);

    private async Task<PageSnapshot> CaptureHtmlAsync(string rows,
        string selectedRaceHeading = "HTMLテストステークス", string? unrelatedHeading = null)
    {
        var unrelated = unrelatedHeading is null ? string.Empty : $"<h2>{unrelatedHeading}</h2><img alt=\"GII\">";
        var page = await _browser.NewPageAsync();
        await page.SetContentAsync($"""
            <main>
              <h1>2026年9月26日（土曜）3回中山3日 3レース</h1>
              <h2>{selectedRaceHeading}</h2>
              {unrelated}
              <p>発走 11:20</p>
              <table>
                <thead><tr><th>枠番</th><th>馬番</th><th>馬名</th><th>性齢/毛色<br>負担重量<br>騎手名</th></tr></thead>
                <tbody>{rows}</tbody>
              </table>
            </main>
            """);
        return await new PlaywrightPageSnapshotter().CaptureAsync(page);
    }

    private static string HtmlRow(string frame, string horseNumber, string horseName,
        string sourceSuffix, bool numberAsAlt = false)
    {
        var numberCell = numberAsAlt
            ? $"<td><img alt=\"{horseNumber}\"></td>"
            : $"<td>{horseNumber}</td>";
        return $"""
            <tr>
              <td>{frame}</td>
              {numberCell}
              <td>
                <div class="name"><a href="/JRADB/accessU.html?CNAME=pw01dud002026{sourceSuffix}/00">{horseName}</a></div>
                <p class="owner">馬主 HTML</p><div class="breeder">HTML牧場</div>
                <div class="trainer">HTML調教師（栗東）</div>
              </td>
              <td>牡3/栗<br>57.0kg<br>HTML騎手</td>
            </tr>
            """;
    }

    private static PageSnapshot Card(
        (string Frame, string HorseNumber, string Name, string? SourceIdentity) first,
        (string Frame, string HorseNumber, string Name, string? SourceIdentity) second,
        bool includeHeaderRow = false,
        string? numberAccessibleName = null,
        string? sourceIdentity = null)
    {
        var header = new[] { "枠番", "馬番", "馬名", "騎手", "斤量" };
        var values = new[] { first, second };
        var rows = new List<PageTableRowSnapshot>();
        rows.Add(new()
        {
            Cells = header.Select(text => new PageTableCellSnapshot { Text = text, IsHeader = true }).ToArray(),
        });
        if (includeHeaderRow)
            rows.Add(new() { Cells = header.Select(text => new PageTableCellSnapshot { Text = text }).ToArray() });

        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var horseSource = value.SourceIdentity ?? sourceIdentity ?? HorseSource($"00000{i + 1}");
            rows.Add(new()
            {
                Cells =
                [
                    new() { Text = value.Frame },
                    new()
                    {
                        Text = value.HorseNumber,
                        Fragments = numberAccessibleName is not null && i == 0
                            ? [new PageElementFragmentSnapshot { TagName = "img", AccessibleName = numberAccessibleName }]
                            : [],
                    },
                    new()
                    {
                        Text = value.Name,
                        Fragments =
                        [
                            new PageElementFragmentSnapshot
                            {
                                TagName = "a", ClassTokens = ["name"], Text = value.Name,
                                RawUrl = horseSource,
                            },
                            new PageElementFragmentSnapshot { TagName = "p", ClassTokens = ["owner"], Text = "馬主 アイコン" },
                            new PageElementFragmentSnapshot { TagName = "div", ClassTokens = ["breeder"], Text = "生産牧場" },
                            new PageElementFragmentSnapshot { TagName = "div", ClassTokens = ["trainer"], Text = "調教師（栗東）" },
                        ],
                    },
                    new() { Text = "牡3/栗\n57.0kg\n騎手" },
                    new() { Text = "57.0kg" },
                ],
            });
        }

        return new PageSnapshot
        {
            Url = new Uri(Url),
            Title = "出馬表",
            Root = new PageContentNode { Kind = PageContentKind.Document },
            Metadata = new PageMetadataSnapshot { Meta = new Dictionary<string, string>(), JsonLd = [] },
            KeyValues = [],
            Tables = [new PageTableSnapshot { Rows = rows }],
            Links = [],
            Images = [],
            Forms = [],
            Diagnostics = [],
        } with
        {
            Root = new PageContentNode
            {
                Kind = PageContentKind.Document,
                Children =
                [
                    new() { Kind = PageContentKind.Heading, Text = "2026年9月26日 中山 3レース" },
                    new() { Kind = PageContentKind.Heading, Text = "テストステークス" },
                    new() { Kind = PageContentKind.Paragraph, Text = "発走 11:20" },
                ],
            },
        };
    }

    private static string HorseSource(string suffix)
        => $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026{suffix}/00";
}

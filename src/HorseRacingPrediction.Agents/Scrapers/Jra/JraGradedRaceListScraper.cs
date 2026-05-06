using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA の重賞レース一覧ページを構造化データへ変換するスクレイパー。
/// </summary>
public sealed class JraGradedRaceListScraper : IScraper<JraGradedRaceListData>
{
    private static readonly HttpClient SharedHttpClient = new();

    private static readonly string[] RacecourseNames =
    [
        "東京", "中山", "阪神", "京都", "中京", "小倉", "函館", "福島", "新潟", "札幌"
    ];

    private static readonly Regex YearRegex = new(@"(20\d{2})年", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GradeAndRaceNameRegex = new(
        @"^(?<grade>J・G[ⅠⅡⅢ]|J・G[123]|G[ⅠⅡⅢ]|G[123])\s+(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RowRegex = new(
        @"<tr[^>]*>(?<cells>.*?)</tr>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex CellRegex = new(
        @"<t[hd][^>]*>(?<cell>.*?)</t[hd]>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex AnchorHrefRegex = new(
        "<a[^>]*href\\s*=\\s*['\\\"](?<href>[^'\\\"#>]+)['\\\"]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IWebBrowser _browser;

    static JraGradedRaceListScraper()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public JraGradedRaceListScraper(IWebBrowser browser)
    {
        _browser = browser;
    }

    public async Task<JraGradedRaceListData?> ScrapeAsync(string url, CancellationToken cancellationToken = default)
    {
        await _browser.NavigateAsync(url, cancellationToken);
        var snapshot = await _browser.GetPageSnapshotAsync(0, cancellationToken);

        var year = ExtractYear(snapshot);
        var races = ParseRacesFromTables(snapshot.Tables, year);
        if (races.Count == 0)
        {
            races = await ParseRacesFromHtmlAsync(url, year, cancellationToken);
        }

        var resultLinks = ExtractRaceResultLinks(snapshot);
        for (var i = 0; i < races.Count && i < resultLinks.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(races[i].ResultUrl))
            {
                races[i] = races[i] with { ResultUrl = resultLinks[i] };
            }
        }

        return new JraGradedRaceListData(url, year, races);
    }

    private static int? ExtractYear(PageSnapshot snapshot)
    {
        var combined = string.Join(" ", new[] { snapshot.Title, string.Join(" ", snapshot.Headings), snapshot.MainText }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        var match = YearRegex.Match(combined);
        return match.Success && int.TryParse(match.Groups[1].Value, out var year)
            ? year
            : null;
    }

    private static List<JraGradedRaceItemData> ParseRacesFromTables(IReadOnlyList<PageTableSnapshot> tables, int? year)
    {
        var results = new List<JraGradedRaceItemData>();

        foreach (var table in tables)
        {
            foreach (var row in table.Rows)
            {
                if (row.Count < 5)
                {
                    continue;
                }

                var dateText = row.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
                var gradeAndName = row.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
                var racecourse = row.ElementAtOrDefault(2)?.Trim() ?? string.Empty;
                var (grade, raceName) = SplitGradeAndRaceName(gradeAndName);

                if (!ContainsRaceDate(dateText) || string.IsNullOrWhiteSpace(grade) || string.IsNullOrWhiteSpace(raceName))
                {
                    continue;
                }

                if (!RacecourseNames.Contains(racecourse, StringComparer.Ordinal))
                {
                    continue;
                }

                results.Add(new JraGradedRaceItemData(
                    ParseRaceDate(year, dateText),
                    NormalizeDateText(dateText),
                    ExtractWeekday(dateText),
                    grade,
                    raceName,
                    racecourse,
                    NullIfEmpty(row.ElementAtOrDefault(3)),
                    NullIfEmpty(row.ElementAtOrDefault(4)),
                    NullIfEmpty(row.ElementAtOrDefault(5)),
                    NullIfEmpty(row.ElementAtOrDefault(6)),
                    null));
            }
        }

        return Deduplicate(results);
    }

    private static async Task<List<JraGradedRaceItemData>> ParseRacesFromHtmlAsync(string url, int? year, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SharedHttpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var html = DecodeHtml(bytes, response.Content.Headers.ContentType?.CharSet);

            var races = new List<JraGradedRaceItemData>();
            foreach (Match rowMatch in RowRegex.Matches(html))
            {
                var cellsHtml = rowMatch.Groups["cells"].Value;
                var cells = CellRegex.Matches(cellsHtml).Select(m => m.Groups["cell"].Value).ToList();
                if (cells.Count < 6)
                {
                    continue;
                }

                var dateText = CleanHtml(cells[0]);
                var (grade, raceName) = SplitGradeAndRaceName(CleanHtml(cells.ElementAtOrDefault(1)));
                var racecourse = CleanHtml(cells.ElementAtOrDefault(2));
                if (!ContainsRaceDate(dateText) || string.IsNullOrWhiteSpace(grade) || string.IsNullOrWhiteSpace(raceName))
                {
                    continue;
                }

                if (!RacecourseNames.Contains(racecourse, StringComparer.Ordinal))
                {
                    continue;
                }

                races.Add(new JraGradedRaceItemData(
                    ParseRaceDate(year, dateText),
                    NormalizeDateText(dateText),
                    ExtractWeekday(dateText),
                    grade,
                    raceName,
                    racecourse,
                    NullIfEmpty(CleanHtml(cells.ElementAtOrDefault(3))),
                    NullIfEmpty(CleanHtml(cells.ElementAtOrDefault(4))),
                    NullIfEmpty(CleanHtml(cells.ElementAtOrDefault(5))),
                    NullIfEmpty(CleanHtml(cells.ElementAtOrDefault(6))),
                    NullIfEmpty(ExtractAnchorHref(cellsHtml, url))));
            }

            return Deduplicate(races);
        }
        catch
        {
            return [];
        }
    }

    private static List<string> ExtractRaceResultLinks(PageSnapshot snapshot)
    {
        var baseUri = Uri.TryCreate(snapshot.Url, UriKind.Absolute, out var parsed)
            ? parsed
            : null;

        return snapshot.Links
            .Where(link => link.Region == "content")
            .Where(link => link.Title.Contains("レース結果", StringComparison.Ordinal))
            .Select(link => NormalizeUrl(link.Url, baseUri))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeUrl(string rawUrl, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            return absolute.ToString();
        }

        if (baseUri is not null && Uri.TryCreate(baseUri, rawUrl, out var relative))
        {
            return relative.ToString();
        }

        return rawUrl;
    }

    private static string? ExtractAnchorHref(string html, string baseUrl)
    {
        var match = AnchorHrefRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            return absolute.ToString();
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, href, out var resolved)
            ? resolved.ToString()
            : href;
    }

    private static string DecodeHtml(byte[] bytes, string? charsetFromHeader)
    {
        var charset = string.IsNullOrWhiteSpace(charsetFromHeader)
            ? DetectCharsetFromMeta(bytes)
            : charsetFromHeader;
        var encoding = TryGetEncoding(charset) ?? Encoding.UTF8;
        return encoding.GetString(bytes);
    }

    private static string? DetectCharsetFromMeta(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes.Take(Math.Min(bytes.Length, 4096)).ToArray());
        var match = Regex.Match(head, "charset\\s*=\\s*['\\\"]?(?<charset>[a-zA-Z0-9_\\-]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["charset"].Value : null;
    }

    private static Encoding? TryGetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static string CleanHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(value);
        var withoutTags = HtmlTagRegex.Replace(decoded, " ");
        return WhitespaceRegex.Replace(withoutTags, " ").Trim();
    }

    private static (string grade, string raceName) SplitGradeAndRaceName(string value)
    {
        var match = GradeAndRaceNameRegex.Match(value);
        if (!match.Success)
        {
            return (string.Empty, value);
        }

        return (NormalizeGrade(match.Groups["grade"].Value), match.Groups["name"].Value.Trim());
    }

    private static string NormalizeGrade(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("Ｇ", "G", StringComparison.Ordinal).Trim();
    }

    private static bool ContainsRaceDate(string text)
        => Regex.IsMatch(text, @"\d{1,2}月\d{1,2}日", RegexOptions.CultureInvariant);

    private static string NormalizeDateText(string text)
    {
        var match = Regex.Match(text, @"(?<date>\d{1,2}月\d{1,2}日)");
        return match.Success ? match.Groups["date"].Value : text.Trim();
    }

    private static string? ExtractWeekday(string text)
    {
        var match = Regex.Match(text, @"(?<weekday>(?:祝日・)?[月火水木金土日]曜)");
        return match.Success ? match.Groups["weekday"].Value : null;
    }

    private static DateOnly? ParseRaceDate(int? year, string dateText)
    {
        if (year is null)
        {
            return null;
        }

        var match = Regex.Match(dateText, @"(?<month>\d{1,2})月(?<day>\d{1,2})日");
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        try
        {
            return new DateOnly(year.Value, month, day);
        }
        catch
        {
            return null;
        }
    }

    private static List<JraGradedRaceItemData> Deduplicate(IReadOnlyList<JraGradedRaceItemData> races)
    {
        return races
            .DistinctBy(r => $"{r.RaceDate:yyyy-MM-dd}|{r.Grade}|{r.RaceName}|{r.Racecourse}")
            .ToList();
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

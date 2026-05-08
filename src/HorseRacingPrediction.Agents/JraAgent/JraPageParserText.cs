using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

internal static class JraPageParserText
{
    private static readonly Regex HoldingLabelRegex = new(
        @"(?<holding>\d+)回(?<racecourse>東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)(?<day>\d+)日",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex YearMonthRegex = new(
        @"(?<year>\d{4})\s*年\s*(?<month>\d{1,2})\s*月",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MonthRegex = new(
        @"(?<!\d)(?<month>\d{1,2})\s*月",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DayRegex = new(
        @"(?<!\d)(?<day>\d{1,2})\s*日",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LeadingDayRegex = new(
        @"^\s*(?<day>\d{1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RaceNumberRegex = new(
        @"(?<number>\d{1,2})\s*(R|レース)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex RacecourseRegex = new(
        @"東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly Regex FeaturedRaceHeaderRegex = new(
        @"(?<month>\d{1,2})月(?<day>\d{1,2})日（[^）]+）\s*[>＞]??\s*(?<raceName>.+?)（(?<grade>G[ⅠⅡⅢ123]|J・G[ⅠⅡⅢ123])）\s*(?<racecourse>東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)競馬場\s*(?<distance>芝\d{3,4}メートル|ダート\d{3,4}メートル)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FullDateRegex = new(
        @"(?<year>\d{4})\s*年\s*(?<month>\d{1,2})\s*月\s*(?<day>\d{1,2})\s*日",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DistanceRegex = new(
        @"(芝|ダート)\s*\d{3,4}\s*メートル",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(string value)
        => value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Trim();

    public static bool ContainsNormalized(string source, string target)
        => Normalize(source).Contains(Normalize(target), StringComparison.Ordinal);

    public static string NormalizeWhitespace(string value)
        => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

    public static string? ResolveUrl(string? sourceUrl, string? candidateUrl)
    {
        if (string.IsNullOrWhiteSpace(candidateUrl))
        {
            return candidateUrl;
        }

        if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var absoluteUri)
            && absoluteUri.IsAbsoluteUri
            && absoluteUri.Scheme is "http" or "https")
        {
            return absoluteUri.ToString();
        }

        if (string.IsNullOrWhiteSpace(sourceUrl)
            || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)
            || !Uri.TryCreate(sourceUri, candidateUrl, out var resolvedUri))
        {
            return candidateUrl;
        }

        return resolvedUri.ToString();
    }

    public static JraStructuredLinkNavigationMode InferNavigationMode(string? url)
        => string.IsNullOrWhiteSpace(url)
            ? JraStructuredLinkNavigationMode.CurrentSessionAction
            : JraStructuredLinkNavigationMode.DirectUrl;

    public static (int? Year, int? Month) ExtractYearMonth(PageSnapshot snapshot)
    {
        var sources = snapshot.Headings
            .Concat(snapshot.Links.Select(link => link.Title))
            .Append(snapshot.Title ?? string.Empty)
            .Append(snapshot.MainText);

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var match = YearMonthRegex.Match(source);
            if (match.Success
                && int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                && int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
            {
                return (year, month);
            }
        }

        var monthOnly = ExtractMonth(snapshot.Title ?? string.Empty)
            ?? snapshot.Headings.Select(ExtractMonth).FirstOrDefault(month => month is not null);
        return (null, monthOnly);
    }

    public static int? ExtractMonth(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = MonthRegex.Match(text);
        return match.Success && int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            ? month
            : null;
    }

    public static int? ExtractDay(IEnumerable<string> sources)
    {
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var match = DayRegex.Match(source);
            if (match.Success && int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            {
                return day;
            }
        }

        return null;
    }

    public static int? ExtractRaceNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = RaceNumberRegex.Match(text);
        return match.Success && int.TryParse(match.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raceNumber)
            ? raceNumber
            : null;
    }

    public static IReadOnlyList<string> ExtractRacecourses(string text)
        => RacecourseRegex.Matches(text ?? string.Empty)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static string? ExtractDistance(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DistanceRegex.Match(text);
        return match.Success ? NormalizeWhitespace(match.Value) : null;
    }

    public static DateOnly? ExtractFirstFullDate(IEnumerable<string> sources)
    {
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var match = FullDateRegex.Match(source);
            if (!match.Success)
            {
                continue;
            }

            if (int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                && int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                && int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            {
                try
                {
                    return new DateOnly(year, month, day);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    public static JraRaceScheduleDay? TryParseScheduleDayCell(string cell, int year, int month)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            return null;
        }

        var dayMatch = LeadingDayRegex.Match(cell);
        if (!dayMatch.Success
            || !int.TryParse(dayMatch.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        var racecourses = ExtractRacecourses(cell);
        if (racecourses.Count == 0)
        {
            return null;
        }

        try
        {
            return new JraRaceScheduleDay(new DateOnly(year, month, day), racecourses, cell);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static IReadOnlyList<JraHoldingEntry> ExtractHoldingEntries(PageSnapshot snapshot)
    {
        var sources = snapshot.Actions.Select(action => action.Text)
            .Concat(snapshot.Links.Select(link => link.Title))
            .Append(snapshot.MainText)
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return sources
            .SelectMany(text => HoldingLabelRegex.Matches(text!).Select(match =>
            {
                var holdingNumber = int.TryParse(match.Groups["holding"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHolding)
                    ? parsedHolding
                    : (int?)null;
                var dayNumber = int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDay)
                    ? parsedDay
                    : (int?)null;
                return new JraHoldingEntry(match.Value, match.Groups["racecourse"].Value, holdingNumber, dayNumber);
            }))
            .DistinctBy(entry => entry.Label)
            .ToList();
    }

    public static string? ExtractG1Slug(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var marker = "/keiba/g1/";
        var index = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var tail = url[(index + marker.Length)..];
        var segment = tail.Split(['/', '.'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(segment) ? null : segment;
    }

    public static bool IsRelevantG1TabUrl(string? slug, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return url.Contains("/keiba/g1/", StringComparison.OrdinalIgnoreCase);
        }

        return url.Contains($"/keiba/g1/{slug}", StringComparison.OrdinalIgnoreCase)
            || url.Contains("datafile", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ExtractRaceNameFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalized = NormalizeWhitespace(title)
            .Replace("　JRA", string.Empty, StringComparison.Ordinal)
            .Replace(" JRA", string.Empty, StringComparison.Ordinal)
            .Trim();

        normalized = Regex.Replace(normalized, @"^\d{4}年", string.Empty).Trim();
        normalized = Regex.Replace(normalized, @"（G[ⅠⅡⅢ123]）$", string.Empty).Trim();

        var separators = new[] { "|", "｜", "-", "－" };
        foreach (var separator in separators)
        {
            var parts = normalized.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && !parts[0].Contains("JRA", StringComparison.Ordinal))
            {
                return parts[0];
            }
        }

        return normalized.Contains("JRA", StringComparison.Ordinal) ? null : normalized;
    }
}
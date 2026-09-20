using System.Text.RegularExpressions;
using System.Globalization;
using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra;

internal static class JraRaceLinkSelector
{
    public static string? FindResultUrl(IEnumerable<(string Url, string Label)> links, RaceId expected,
        IReadOnlyList<string> purposeLabels, string? baseUrl = null)
    {
        foreach (var link in links.Where(link => !string.IsNullOrWhiteSpace(link.Url)
                                                && purposeLabels.Any(purpose =>
                                                    link.Label.Contains(purpose, StringComparison.Ordinal))))
        {
            var normalized = NormalizeUrl(baseUrl, link.Url);
            if (normalized is not null && TryGetResultRaceId(normalized, out var actual) && actual == expected)
                return link.Url;
        }

        return null;
    }

    public static string? FindUrl(IEnumerable<(string Url, string Label)> links, int raceNumber,
        IReadOnlyList<string> purposeLabels, string? baseUrl = null, bool allowNumberOnly = false)
    {
        var candidates = links
            .Where(link => !string.IsNullOrWhiteSpace(link.Url) && link.Url != "#")
            .Select(link => new Candidate(link.Url, NormalizeUrl(baseUrl, link.Url), link.Label))
            .Where(candidate => candidate.NormalizedUrl is not null)
            .ToArray();
        var numberPattern = new Regex($@"(^|\D){raceNumber}\s*(?:R|レース)(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var group in candidates.GroupBy(link => link.NormalizedUrl!, StringComparer.OrdinalIgnoreCase))
        {
            if (!group.Any(link => numberPattern.IsMatch(link.Label))) continue;
            if (group.Any(link => purposeLabels.Any(purpose =>
                    link.Label.Contains(purpose, StringComparison.Ordinal))))
                return group.First(link => numberPattern.IsMatch(link.Label)).RawUrl;
        }

        return allowNumberOnly
            ? candidates.FirstOrDefault(link => numberPattern.IsMatch(link.Label))?.RawUrl
            : null;
    }

    private static string? NormalizeUrl(string? baseUrl, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https")
        {
            return absolute.AbsoluteUri;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) ||
            origin.Scheme is not ("http" or "https"))
        {
            return baseUrl is null ? url : null;
        }

        // On Unix, Uri can interpret a root-relative path as an absolute file URI.
        // Join it to the HTTP origin explicitly so URL equivalence is platform independent.
        if (url.StartsWith("/", StringComparison.Ordinal) && !url.StartsWith("//", StringComparison.Ordinal))
        {
            return Uri.TryCreate(origin.GetLeftPart(UriPartial.Authority) + url, UriKind.Absolute, out var rootRelative)
                ? rootRelative.AbsoluteUri
                : null;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return Uri.TryCreate($"{origin.Scheme}:{url}", UriKind.Absolute, out var protocolRelative)
                ? protocolRelative.AbsoluteUri
                : null;
        }

        return Uri.TryCreate(url, UriKind.Relative, out var relative) && Uri.TryCreate(origin, relative, out var resolved)
            ? resolved.AbsoluteUri
            : null;
    }

    private static bool TryGetResultRaceId(string url, out RaceId race)
    {
        race = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var resolved)
            || resolved.Scheme is not ("http" or "https")
            || !resolved.Host.Equals("www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            || !resolved.AbsolutePath.Equals("/JRADB/accessS.html", StringComparison.OrdinalIgnoreCase))
            return false;

        var match = Regex.Match(Uri.UnescapeDataString(resolved.Query),
            @"(?:^|[?&])CNAME=pw01sde(?:01|10)(?<course>\d{2})\d{8}(?<number>\d{2})(?<date>\d{8})/",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success
            || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)
            || !int.TryParse(match.Groups["number"].Value, CultureInfo.InvariantCulture, out var number))
            return false;

        var course = match.Groups["course"].Value switch
        {
            "01" => RaceCourse.Sapporo,
            "02" => RaceCourse.Hakodate,
            "03" => RaceCourse.Fukushima,
            "04" => RaceCourse.Niigata,
            "05" => RaceCourse.Tokyo,
            "06" => RaceCourse.Nakayama,
            "07" => RaceCourse.Chukyo,
            "08" => RaceCourse.Kyoto,
            "09" => RaceCourse.Hanshin,
            "10" => RaceCourse.Kokura,
            _ => RaceCourse.Unknown,
        };
        if (course == RaceCourse.Unknown) return false;
        race = new(date, course, number);
        return true;
    }

    private sealed record Candidate(string RawUrl, string? NormalizedUrl, string Label);
}

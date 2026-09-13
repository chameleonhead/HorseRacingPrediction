using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Scraping.Jra;

internal static class JraRaceLinkSelector
{
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
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme is "http" or "https" ? absolute.AbsoluteUri : null;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) &&
               Uri.TryCreate(origin, url, out var resolved) &&
               resolved.Scheme is "http" or "https"
            ? resolved.AbsoluteUri
            : baseUrl is null ? url : null;
    }

    private sealed record Candidate(string RawUrl, string? NormalizedUrl, string Label);
}

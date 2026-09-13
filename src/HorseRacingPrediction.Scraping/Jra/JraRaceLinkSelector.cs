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

    private sealed record Candidate(string RawUrl, string? NormalizedUrl, string Label);
}

using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

internal static partial class JraRaceDetailUrl
{
    internal sealed record SourceIdentity(int Year, RaceCourse Course, int MeetingNumber, int MeetingDay, int RaceNumber);

    public static Uri? Validate(Uri? url, ResourceType type, RaceId expected)
    {
        if (url is null || url.Scheme is not ("http" or "https")
            || !string.Equals(url.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)) return null;
        var path = type switch
        {
            ResourceType.RaceCard => "/JRADB/accessD.html",
            ResourceType.RaceResult => "/JRADB/accessS.html",
            _ => string.Empty,
        };
        if (!string.Equals(url.AbsolutePath, path, StringComparison.OrdinalIgnoreCase)) return null;
        var values = ParseQuery(url.Query).Where(x => x.Key.Equals("CNAME", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value).ToArray();
        if (values is not { Length: 1 } || string.IsNullOrWhiteSpace(values[0])) return null;
        var match = CNamePattern().Match(Uri.UnescapeDataString(values[0]));
        if (!match.Success || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)
            || !int.TryParse(match.Groups["number"].Value, CultureInfo.InvariantCulture, out var number)
            || !CourseCodes.TryGetValue(match.Groups["course"].Value, out var course)
            || date != expected.Date || number != expected.Number || course != expected.Course) return null;
        return url;
    }

    public static bool TryGetSourceIdentity(Uri? url, out SourceIdentity identity)
    {
        identity = null!;
        if (url is null || !string.Equals(url.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)) return false;
        var value = ParseQuery(url.Query).SingleOrDefault(x => x.Key.Equals("CNAME", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = CNamePattern().Match(Uri.UnescapeDataString(value));
        if (!match.Success
            || !int.TryParse(match.Groups["year"].Value, out var year)
            || !int.TryParse(match.Groups["meeting"].Value, out var meeting)
            || !int.TryParse(match.Groups["day"].Value, out var day)
            || !int.TryParse(match.Groups["number"].Value, out var number)
            || !CourseCodes.TryGetValue(match.Groups["course"].Value, out var course)) return false;
        identity = new(year, course, meeting, day, number);
        return true;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            yield return new(Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? pair[1] : string.Empty);
        }
    }

    [GeneratedRegex(@"^pw01(?:dde|sde)(?:01|10)(?<course>\d{2})(?<year>\d{4})(?<meeting>\d{2})(?<day>\d{2})(?<number>\d{2})(?<date>\d{8})/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CNamePattern();

    private static readonly IReadOnlyDictionary<string, RaceCourse> CourseCodes =
        new Dictionary<string, RaceCourse>(StringComparer.Ordinal)
        {
            ["01"] = RaceCourse.Sapporo,
            ["02"] = RaceCourse.Hakodate,
            ["03"] = RaceCourse.Fukushima,
            ["04"] = RaceCourse.Niigata,
            ["05"] = RaceCourse.Tokyo,
            ["06"] = RaceCourse.Nakayama,
            ["07"] = RaceCourse.Chukyo,
            ["08"] = RaceCourse.Kyoto,
            ["09"] = RaceCourse.Hanshin,
            ["10"] = RaceCourse.Kokura,
        };
}

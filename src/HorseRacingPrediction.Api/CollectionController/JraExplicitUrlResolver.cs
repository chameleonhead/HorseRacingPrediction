using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.CollectionController;

internal static partial class JraExplicitUrlResolver
{
    private static readonly IReadOnlyDictionary<string, (string Name, string ResourceName)> Courses =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["01"] = ("札幌", "Sapporo"), ["02"] = ("函館", "Hakodate"),
            ["03"] = ("福島", "Fukushima"), ["04"] = ("新潟", "Niigata"),
            ["05"] = ("東京", "Tokyo"), ["06"] = ("中山", "Nakayama"),
            ["07"] = ("中京", "Chukyo"), ["08"] = ("京都", "Kyoto"),
            ["09"] = ("阪神", "Hanshin"), ["10"] = ("小倉", "Kokura"),
        };

    public static ExplicitUrlCollectionResult Resolve(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase))
            return Unidentified("JRAのHTTPS URLを入力してください。");

        var cname = ParseQuery(uri.Query).GetValueOrDefault("CNAME");
        if (string.IsNullOrWhiteSpace(cname))
            return Unidentified("URLからJRAページの識別情報を取得できませんでした。");

        var (resourceType, definition) = cname switch
        {
            var value when value.StartsWith("pw01sde", StringComparison.OrdinalIgnoreCase) =>
                (ResourceType.RaceResult, new CollectionDefinitionId("race-result")),
            var value when value.StartsWith("pw01dde", StringComparison.OrdinalIgnoreCase) =>
                (ResourceType.RaceCard, new CollectionDefinitionId("race-card")),
            _ => ((ResourceType?)null, (CollectionDefinitionId?)null),
        };
        if (resourceType is null || definition is null)
            return Unidentified("この種類のJRAページはURLだけでは収集対象を識別できません。");

        var match = RaceIdentityRegex().Match(cname);
        if (!match.Success || !Courses.TryGetValue(match.Groups["course"].Value, out var course)
            || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)
            || !int.TryParse(match.Groups["number"].Value, CultureInfo.InvariantCulture, out var number)
            || number is < 1 or > 12)
            return Unidentified("URLから開催日・競馬場・レース番号を識別できませんでした。");

        var resource = new ResourceKey(resourceType.Value, "JRA",
            $"{date:yyyyMMdd}:{course.ResourceName}:{number}");
        var attributes = new Dictionary<string, string>
        {
            ["course"] = course.Name,
            ["number"] = number.ToString(CultureInfo.InvariantCulture),
        };
        return new(true, resource, definition, date, attributes, uri.AbsoluteUri, null, null, null);
    }

    private static ExplicitUrlCollectionResult Unidentified(string message) =>
        new(false, null, null, null, new Dictionary<string, string>(), null,
            "UnidentifiedExplicitLocation", message, null);

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Split('=', 2))
        .Where(parts => parts.Length == 2)
        .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1]),
            StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^pw01(?:sde|dde)10(?<course>\d{2})\d{8}(?<number>\d{2})(?<date>\d{8})/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RaceIdentityRegex();
}

public sealed record CreateExplicitUrlCollectionRequest(string Url);

public sealed record ExplicitUrlCollectionResult(bool Identified, ResourceKey? Resource,
    CollectionDefinitionId? Definition, DateOnly? EffectiveDate, IReadOnlyDictionary<string, string> Attributes,
    string? ExplicitUrl, string? ErrorCode, string? ErrorMessage, CollectionRequestReceipt? Receipt);

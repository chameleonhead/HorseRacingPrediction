using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.CollectionController;

internal static partial class JraExplicitUrlResolver
{
    private const int MaxUrlLength = 4096;
    private const int MaxCnameLength = 512;
    private static readonly IReadOnlyDictionary<string, (string Name, string ResourceName)> Courses =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["01"] = ("札幌", "Sapporo"),
            ["02"] = ("函館", "Hakodate"),
            ["03"] = ("福島", "Fukushima"),
            ["04"] = ("新潟", "Niigata"),
            ["05"] = ("東京", "Tokyo"),
            ["06"] = ("中山", "Nakayama"),
            ["07"] = ("中京", "Chukyo"),
            ["08"] = ("京都", "Kyoto"),
            ["09"] = ("阪神", "Hanshin"),
            ["10"] = ("小倉", "Kokura"),
        };

    public static ExplicitUrlCollectionResult Resolve(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength || !HasValidPercentEncoding(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
            return Unidentified("JRAのHTTPS URLを入力してください。");

        if (!TryGetCname(uri.Query, out var cname) || string.IsNullOrWhiteSpace(cname)
            || cname.Length > MaxCnameLength)
            return Unidentified("URLからJRAページの識別情報を取得できませんでした。");

        (ResourceType? resourceType, CollectionDefinitionId? definition, string? expectedPath) = cname switch
        {
            var value when value.StartsWith("pw01sde", StringComparison.OrdinalIgnoreCase) =>
                ((ResourceType?)ResourceType.RaceResult,
                    (CollectionDefinitionId?)new CollectionDefinitionId("race-result"), (string?)"/JRADB/accessS.html"),
            var value when value.StartsWith("pw01dde", StringComparison.OrdinalIgnoreCase) =>
                ((ResourceType?)ResourceType.RaceCard,
                    (CollectionDefinitionId?)new CollectionDefinitionId("race-card"), (string?)"/JRADB/accessD.html"),
            _ => ((ResourceType?)null, (CollectionDefinitionId?)null, null),
        };
        if (resourceType is null || definition is null || !string.Equals(uri.AbsolutePath, expectedPath,
                StringComparison.Ordinal))
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

    private static bool TryGetCname(string query, out string? cname)
    {
        cname = null;
        var found = false;
        foreach (var value in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = value.Split('=', 2);
            if (parts.Length != 2)
                continue;
            if (!TryUnescape(parts[0], out var key) || !TryUnescape(parts[1], out var decoded))
                return false;
            if (!string.Equals(key, "CNAME", StringComparison.OrdinalIgnoreCase))
                continue;
            if (found)
                return false;
            found = true;
            cname = decoded;
        }
        return found;
    }

    private static bool TryUnescape(string value, out string decoded)
    {
        decoded = Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
        return !decoded.Contains('\uFFFD');
    }

    private static bool HasValidPercentEncoding(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
                continue;
            if (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2]))
                return false;
            index += 2;
        }
        return true;
    }

    [GeneratedRegex(@"^pw01(?:sde|dde)10(?<course>\d{2})\d{8}(?<number>\d{2})(?<date>\d{8})/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RaceIdentityRegex();
}

public sealed record CreateExplicitUrlCollectionRequest(string? Url);

public sealed record ExplicitUrlCollectionResult(bool Identified, ResourceKey? Resource,
    CollectionDefinitionId? Definition, DateOnly? EffectiveDate, IReadOnlyDictionary<string, string> Attributes,
    string? ExplicitUrl, string? ErrorCode, string? ErrorMessage, CollectionRequestReceipt? Receipt);

using System.Web;

namespace HorseRacingPrediction.Contracts.Common;

public static class JraSourceIdentity
{
    private const string HorsePath = "/JRADB/accessU.html";
    private const string HorseCnamePrefix = "pw01dud";
    private const int HorseNumberLength = 10;

    public static bool MatchesHorse(string? left, string? right) =>
        TryNormalizeHorse(left, out var leftIdentity)
        && TryNormalizeHorse(right, out var rightIdentity)
        && string.Equals(leftIdentity, rightIdentity, StringComparison.Ordinal);

    public static bool TryNormalizeHorse(string? value, out string identity)
    {
        identity = string.Empty;
        if (!TryParseHorse(value, out _, out var cname)) return false;

        var separator = cname.IndexOf('/');
        var route = separator > 0 ? cname[..separator] : string.Empty;
        var horseNumberStart = HorseCnamePrefix.Length + 2;
        var hasStableHorseNumber = separator > 0
            && route.Length == horseNumberStart + HorseNumberLength
            && route.StartsWith(HorseCnamePrefix, StringComparison.OrdinalIgnoreCase)
            && route.AsSpan(HorseCnamePrefix.Length, 2).ToString().All(char.IsDigit)
            && route.AsSpan(horseNumberStart, HorseNumberLength).ToString().All(char.IsDigit);
        identity = hasStableHorseNumber
            ? $"jra-horse:{route[horseNumberStart..]}"
            : cname;
        return true;
    }

    public static Uri? NormalizeHorseUrl(string? value)
    {
        if (!TryParseHorse(value, out _, out var cname) || !TryNormalizeHorse(value, out _)) return null;
        return new Uri($"https://www.jra.go.jp{HorsePath}?CNAME={cname}");
    }

    private static bool TryParseHorse(string? value, out Uri uri, out string cname)
    {
        uri = null!;
        cname = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            if (!string.Equals(absolute.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)) return false;
            uri = absolute;
        }
        else if (Uri.TryCreate(new Uri("https://www.jra.go.jp"), value, out var relative))
        {
            uri = relative;
        }
        else return false;

        if (uri.Scheme is not ("http" or "https")
            || !string.Equals(uri.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.AbsolutePath, HorsePath, StringComparison.OrdinalIgnoreCase)) return false;
        var values = HttpUtility.ParseQueryString(uri.Query).GetValues("CNAME");
        if (values is not { Length: 1 } || string.IsNullOrWhiteSpace(values[0])) return false;
        cname = values[0]!.Trim();
        return cname.All(character => char.IsLetterOrDigit(character) || character is '/' or '_' or '-');
    }
}

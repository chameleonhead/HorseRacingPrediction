using System.Web;

namespace HorseRacingPrediction.ApiClient;

public static class JraSourceIdentity
{
    private const string HorsePath = "/JRADB/accessU.html";

    public static bool TryNormalizeHorse(string? value, out string identity)
    {
        identity = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        Uri? uri;
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

        if (!string.Equals(uri.AbsolutePath, HorsePath, StringComparison.OrdinalIgnoreCase)) return false;
        var values = HttpUtility.ParseQueryString(uri.Query).GetValues("CNAME");
        if (values is not { Length: 1 } || string.IsNullOrWhiteSpace(values[0])) return false;
        identity = values[0]!.Trim();
        if (identity.Any(character => !char.IsLetterOrDigit(character) && character is not ('/' or '_' or '-')))
        {
            identity = string.Empty;
            return false;
        }
        return true;
    }

    public static Uri? NormalizeHorseUrl(string? value)
        => TryNormalizeHorse(value, out var identity)
            ? new Uri($"https://www.jra.go.jp{HorsePath}?CNAME={identity}")
            : null;
}

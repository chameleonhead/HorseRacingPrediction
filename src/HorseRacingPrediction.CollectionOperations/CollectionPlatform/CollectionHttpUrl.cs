namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public static class CollectionHttpUrl
{
    public static bool TryCreate(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) || !IsHttp(candidate)) return false;
        uri = candidate;
        return true;
    }

    public static Uri? Resolve(string? value, string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (TryCreate(value, out var absolute)) return absolute;
        if (!TryCreate(sourceUrl, out var source) || !Uri.TryCreate(source, value, out var resolved)
            || !IsHttp(resolved)) return null;
        return resolved;
    }

    public static void EnsureHttp(Uri? uri, string parameterName)
    {
        if (uri is not null && !IsHttp(uri))
            throw new ArgumentException("Only absolute HTTP(S) URLs are supported.", parameterName);
    }

    private static bool IsHttp(Uri uri) => uri.IsAbsoluteUri
        && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
}

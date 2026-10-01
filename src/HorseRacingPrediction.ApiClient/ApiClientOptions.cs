namespace HorseRacingPrediction.ApiClient;

public sealed class ApiClientOptions
{
    public Uri? BaseAddress { get; set; }
    public string? ApiKey { get; set; }
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    internal void Validate()
    {
        if (BaseAddress is not { IsAbsoluteUri: true }
            || (BaseAddress.Scheme != Uri.UriSchemeHttp && BaseAddress.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("BaseAddress must be an absolute HTTP or HTTPS URI.", nameof(BaseAddress));

        if (string.IsNullOrWhiteSpace(ApiKeyHeaderName))
            throw new ArgumentException("ApiKeyHeaderName must be a valid HTTP header token.", nameof(ApiKeyHeaderName));

        try
        {
            using var request = new HttpRequestMessage();
            request.Headers.Add(ApiKeyHeaderName, "validation");
        }
        catch (FormatException)
        {
            throw new ArgumentException("ApiKeyHeaderName must be a valid HTTP header token.", nameof(ApiKeyHeaderName));
        }

        if (Timeout != System.Threading.Timeout.InfiniteTimeSpan && Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive or InfiniteTimeSpan.");

        if (!string.IsNullOrEmpty(ApiKey))
        {
            try
            {
                using var request = new HttpRequestMessage();
                request.Headers.Add(ApiKeyHeaderName, ApiKey);
            }
            catch (FormatException)
            {
                throw new ArgumentException("ApiKey is not a valid HTTP header value.", nameof(ApiKey));
            }
        }
    }
}

using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Http;

public sealed class ApiClientOptionsValidator : IValidateOptions<ApiClientOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiClientOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            failures.Add("ApiClient:BaseUrl は必須です。");
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("ApiClient:BaseUrl には http または https の絶対 URL を指定してください。");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("ApiClient:ApiKey は必須です。");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
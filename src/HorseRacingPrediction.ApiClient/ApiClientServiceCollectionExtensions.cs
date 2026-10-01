using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HorseRacingPrediction.ApiClient;

public static class ApiClientServiceCollectionExtensions
{
    public static IHttpClientBuilder AddHorseRacingApiClient(
        this IServiceCollection services,
        Action<ApiClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ApiClientOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton<IApiClientFactory, ApiClientFactory>();
        return services.AddHttpClient(ApiClientFactory.HttpClientName, client =>
        {
            client.BaseAddress = options.BaseAddress!;
            client.Timeout = options.Timeout;
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                client.DefaultRequestHeaders.Remove(options.ApiKeyHeaderName);
                client.DefaultRequestHeaders.Add(options.ApiKeyHeaderName, options.ApiKey);
            }
        });
    }
}

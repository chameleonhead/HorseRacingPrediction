using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Http;

/// <summary>
/// <see cref="HttpRaceQueryService"/>、<see cref="HttpPredictionWriteService"/>、
/// <see cref="HttpDataCollectionWriteService"/> を DI コンテナに登録する拡張メソッドを提供する。
/// </summary>
public static class AgentHttpServiceCollectionExtensions
{
    /// <summary>
    /// クラウド API への HTTP 接続設定と HTTP 実装サービスを DI コンテナに登録する。
    /// <para>
    /// appsettings.json の <c>ApiClient</c> セクションに <c>BaseUrl</c> と <c>ApiKey</c> を設定してください。
    /// </para>
    /// </summary>
    public static IServiceCollection AddHttpAgentServices(this IServiceCollection services)
    {
        services.AddHttpClient<IRaceQueryService, HttpRaceQueryService>(ConfigureClient);
        services.AddHttpClient<IPredictionWriteService, HttpPredictionWriteService>(ConfigureClient);
        services.AddHttpClient<IDataCollectionWriteService, HttpDataCollectionWriteService>(ConfigureClient);

        services.AddTransient<RaceQueryTools>();
        services.AddTransient<PredictionWriteTools>();
        services.AddTransient<DataCollectionWriteTools>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider sp, HttpClient client)
    {
        var options = sp.GetRequiredService<IOptions<ApiClientOptions>>().Value;

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            client.BaseAddress = new Uri(options.BaseUrl);

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    }
}

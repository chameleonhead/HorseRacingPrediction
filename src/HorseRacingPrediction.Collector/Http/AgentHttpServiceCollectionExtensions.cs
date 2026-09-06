using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Http;

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
        services.AddSingleton<AgentAcquisitionStatusRecorder>();
        services.AddTransient<TransientBadGatewayRetryHandler>();
        services.AddHttpClient<IRaceQueryService, HttpRaceQueryService>(ConfigureClient)
            .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
        services.AddHttpClient<IPredictionWriteService, HttpPredictionWriteService>(ConfigureClient)
            .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
        services.AddHttpClient<IDataCollectionWriteService, HttpDataCollectionWriteService>(ConfigureClient)
            .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
        services.AddHttpClient<IMemoWriteService, HttpMemoWriteService>(ConfigureClient)
            .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();

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

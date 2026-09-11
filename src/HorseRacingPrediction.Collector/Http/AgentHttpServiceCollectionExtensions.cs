using HorseRacingPrediction.ApiClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Http;

/// <summary>
/// <see cref="HttpRaceQueryService"/>、<see cref="HttpPredictionWriteService"/>、
/// 予測・参照用 HTTP クライアントを DI コンテナに登録する拡張メソッドを提供する。
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
        services.AddTransient<TransientBadGatewayRetryHandler>();
        services.AddTransient<CollectionWorkerLeaseHandler>();
        services.AddSingleton<AgentAcquisitionStatusRecorder>();
        services.AddHttpClient<IRaceQueryService, HttpRaceQueryService>(ConfigureClient)
            .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
        services.AddHttpClient<IPredictionWriteService, HttpPredictionWriteService>(ConfigureClient)
            .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
        services.AddHttpClient<IDataCollectionWriteService, HttpDataCollectionWriteService>(ConfigureClient)
            .AddHttpMessageHandler<CollectionWorkerLeaseHandler>()
            .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();
        services.AddHttpClient<IMemoWriteService, HttpMemoWriteService>(ConfigureClient)
            .AddHttpMessageHandler<TransientBadGatewayRetryHandler>();

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

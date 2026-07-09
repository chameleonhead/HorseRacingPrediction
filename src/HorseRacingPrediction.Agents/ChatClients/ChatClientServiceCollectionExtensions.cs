using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Agents.ChatClients;

/// <summary>
/// 本番ホスティング向けの <see cref="IChatClient"/> を DI コンテナに登録する拡張メソッドを提供する。
/// </summary>
public static class ChatClientServiceCollectionExtensions
{
    /// <summary>
    /// LM Studio（<c>LMStudio</c> セクションの <c>BaseUrl</c> / <c>Model</c>）に接続する
    /// <see cref="IChatClient"/> を DI コンテナに登録する。
    /// </summary>
    public static IServiceCollection AddLMStudioChatClient(this IServiceCollection services)
    {
        services.AddSingleton<IChatClient>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["LMStudio:BaseUrl"] ?? "http://localhost:1234";
            var model = configuration["LMStudio:Model"] ?? "default";

            var options = new LMStudioChatClientOptions
            {
                BaseUri = new Uri(baseUrl),
                DefaultModel = model,
            };

            return new LMStudioChatClient(options);
        });

        return services;
    }
}

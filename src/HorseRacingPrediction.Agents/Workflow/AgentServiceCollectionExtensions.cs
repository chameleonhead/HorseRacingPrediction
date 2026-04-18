using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// エージェント関連サービスを DI コンテナに登録する拡張メソッドを提供する。
/// </summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// WebBrowserAgent および WebFetchTools を DI コンテナに登録する。
    /// <para>
    /// 使用例（Program.cs または テスト初期化）:
    /// <code>
    /// builder.Services.AddWebBrowserAgent();
    /// builder.Services.Configure&lt;WebFetchOptions&gt;(
    ///     builder.Configuration.GetSection(WebFetchOptions.SectionName));
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddWebBrowserAgent(this IServiceCollection services)
    {
        services.AddTransient<WebFetchTools>();
        services.AddTransient<WebBrowserAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var browser = sp.GetRequiredService<IWebBrowser>();
            var options = sp.GetRequiredService<IOptions<WebFetchOptions>>();

            var webFetchTools = new WebFetchTools(browser, options);
            return new WebBrowserAgent(chatClient, webFetchTools.GetAITools());
        });
        return services;
    }

    /// <summary>
    /// <see cref="DataCollectionWorkflow"/> および 4 つのデータ収集エージェントを
    /// DI コンテナに登録する。
    /// <para>
    /// 使用例（Program.cs または テスト初期化）:
    /// <code>
    /// builder.Services.AddDataCollectionWorkflow();
    /// builder.Services.Configure&lt;WebFetchOptions&gt;(
    ///     builder.Configuration.GetSection(WebFetchOptions.SectionName));
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddDataCollectionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<WebFetchTools>();
        services.AddTransient<RaceDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
            return new RaceDataAgent(chatClient, tools);
        });
        services.AddTransient<HorseDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
            return new HorseDataAgent(chatClient, tools);
        });
        services.AddTransient<JockeyDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
            return new JockeyDataAgent(chatClient, tools);
        });
        services.AddTransient<StableDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
            return new StableDataAgent(chatClient, tools);
        });
        services.AddTransient<DataCollectionWorkflow>();
        return services;
    }
}


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
    /// 各データ収集エージェントは <see cref="WebBrowserAgent"/> を介して
    /// Web 情報にアクセスする。
    /// </para>
    /// <para>
    /// 使用例（Program.cs または テスト初期化）:
    /// <code>
    /// builder.Services.AddWebBrowserAgent();
    /// builder.Services.AddDataCollectionWorkflow();
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddDataCollectionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<RaceDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
            return new RaceDataAgent(chatClient, [webBrowserAgent.CreateAIFunction()]);
        });
        services.AddTransient<HorseDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
            return new HorseDataAgent(chatClient, [webBrowserAgent.CreateAIFunction()]);
        });
        services.AddTransient<JockeyDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
            return new JockeyDataAgent(chatClient, [webBrowserAgent.CreateAIFunction()]);
        });
        services.AddTransient<StableDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
            return new StableDataAgent(chatClient, [webBrowserAgent.CreateAIFunction()]);
        });
        services.AddTransient<DataCollectionWorkflow>();
        return services;
    }

    /// <summary>
    /// <see cref="WeeklyScheduleWorkflow"/> を DI コンテナに登録する。
    /// <para>
    /// 使用例（Program.cs または テスト初期化）:
    /// <code>
    /// builder.Services.AddWebBrowserAgent();
    /// builder.Services.AddWeeklyScheduleWorkflow();
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddWeeklyScheduleWorkflow(this IServiceCollection services)
    {
        services.AddTransient<WeeklyScheduleWorkflow>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
            return WeeklyScheduleWorkflow.Create(chatClient, webBrowserAgent);
        });
        return services;
    }
}


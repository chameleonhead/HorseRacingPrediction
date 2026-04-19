using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// エージェント関連サービスを DI コンテナに登録する拡張メソッドを提供する。
/// </summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// PlaywrightTools、WebBrowserAgent、WebFetchTools、および HorseRacingTools を DI コンテナに登録する。
    /// <para>
    /// PlaywrightTools は Playwright ベースの低レベルブラウザ操作（ページ移動・リンク抽出・検索）を提供し、
    /// WebBrowserAgent はこれらを AI ツールとして使用して自律的に Web 調査を行う。
    /// WebFetchTools は WebBrowserAgent に委譲する高レベル API を提供し、
    /// HorseRacingTools は競馬固有の情報取得ツールを提供する。
    /// </para>
    /// <para>
    /// 依存チェーン: IWebBrowser → PlaywrightTools → WebBrowserAgent → WebFetchTools → HorseRacingTools
    /// </para>
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
        services.AddSingleton<PageDataExtractionAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new PageDataExtractionAgent(chatClient);
        });
        services.AddTransient<PlaywrightTools>();
        services.AddTransient<WebBrowserAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var browser = sp.GetRequiredService<IWebBrowser>();
            var options = sp.GetRequiredService<IOptions<WebFetchOptions>>();
            var extractionAgent = sp.GetRequiredService<PageDataExtractionAgent>();
            var logger = sp.GetRequiredService<ILogger<PlaywrightTools>>();

            var playwrightTools = new PlaywrightTools(browser, options, extractionAgent, logger);
            return new WebBrowserAgent(chatClient, playwrightTools.GetAITools());
        });
        services.AddTransient<WebFetchTools>(sp =>
        {
            var agent = sp.GetRequiredService<WebBrowserAgent>();
            return new WebFetchTools(agent);
        });
        services.AddTransient<HorseRacingTools>(sp =>
        {
            var webFetchTools = sp.GetRequiredService<WebFetchTools>();
            return new HorseRacingTools(webFetchTools);
        });
        return services;
    }

    /// <summary>
    /// <see cref="DataCollectionWorkflow"/> および 4 つのデータ収集エージェントを
    /// DI コンテナに登録する。
    /// <para>
    /// 各データ収集エージェントは <see cref="HorseRacingTools"/> の競馬固有ツールと、
    /// <see cref="WebBrowserAgent"/> 経由の汎用 Web 検索機能を併用する。
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
            var horseRacingTools = sp.GetRequiredService<HorseRacingTools>();
            var tools = new List<AITool>(horseRacingTools.GetAITools())
            {
                webBrowserAgent.CreateAIFunction()
            };
            return new RaceDataAgent(chatClient, tools);
        });
        services.AddTransient<HorseDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
            var horseRacingTools = sp.GetRequiredService<HorseRacingTools>();
            var tools = new List<AITool>(horseRacingTools.GetAITools())
            {
                webBrowserAgent.CreateAIFunction()
            };
            return new HorseDataAgent(chatClient, tools);
        });
        services.AddTransient<JockeyDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
            var horseRacingTools = sp.GetRequiredService<HorseRacingTools>();
            var tools = new List<AITool>(horseRacingTools.GetAITools())
            {
                webBrowserAgent.CreateAIFunction()
            };
            return new JockeyDataAgent(chatClient, tools);
        });
        services.AddTransient<StableDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            var webBrowserAgent = sp.GetRequiredService<WebBrowserAgent>();
            var horseRacingTools = sp.GetRequiredService<HorseRacingTools>();
            var tools = new List<AITool>(horseRacingTools.GetAITools())
            {
                webBrowserAgent.CreateAIFunction()
            };
            return new StableDataAgent(chatClient, tools);
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


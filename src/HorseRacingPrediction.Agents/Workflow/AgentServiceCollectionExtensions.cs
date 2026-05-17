using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
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
            var configuration = sp.GetService<IConfiguration>();
            var modelId = ResolveConfiguredModelId(configuration);
            var profileOverride = configuration?["Agents:PageExtraction:Profile"];
            return new PageDataExtractionAgent(chatClient, modelId: modelId, profileOverride: profileOverride);
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
        services.AddSingleton<JraPageExtractionTools>();
        services.AddTransient<JraRaceCardScraper>(sp =>
        {
            var browser = sp.GetRequiredService<IWebBrowser>();
            return new JraRaceCardScraper(browser);
        });
        return services;
    }

    private static string? ResolveConfiguredModelId(IConfiguration? configuration)
    {
        return configuration?["LMStudio:Model"]
            ?? configuration?["OpenAI:Model"]
            ?? Environment.GetEnvironmentVariable("LMSTUDIO_MODEL")
            ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? Environment.GetEnvironmentVariable("LLM_MODEL");
    }

    /// <summary>
    /// <see cref="JraRaceResultCollectionWorkflow"/>、<see cref="JraRaceResultUrlDiscoveryAgent"/>、
    /// および <see cref="JraRaceResultScraper"/> を DI コンテナに登録する。
    /// <para>
    /// このワークフローは AI が成績 URL を発見し、
    /// Playwright が各ページをスクレイプして DB へ保存するという構成になっており、
    /// AI によるページ読み取りを最小限に抑えてトークン消費を削減する。
    /// </para>
    /// <para>
    /// 使用例（Program.cs または テスト初期化）:
    /// <code>
    /// builder.Services.AddHorseRacingAgentDomainSupport(connectionString);
    /// builder.Services.AddWebBrowserAgent();
    /// builder.Services.AddJraRaceResultCollectionWorkflow();
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddJraRaceResultCollectionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<JraRaceResultScraper>(sp =>
        {
            var browser = sp.GetRequiredService<IWebBrowser>();
            return new JraRaceResultScraper(browser);
        });
        services.AddTransient<JraRaceResultCollectionWorkflow>(sp =>
            new JraRaceResultCollectionWorkflow(
                sp.GetRequiredService<IWebBrowser>(),
                sp.GetRequiredService<JraRaceResultScraper>(),
                sp.GetRequiredService<DataCollectionWriteTools>(),
                sp.GetRequiredService<IRaceQueryService>(),
                sp.GetService<ILogger<JraRaceResultCollectionWorkflow>>(),
                sp.GetService<ILoggerFactory>()));
        return services;
    }

    /// <summary>
    /// <see cref="JraRaceScheduleCollectionWorkflow"/> を DI コンテナに登録する。
    /// <para>
    /// JRA サイト構成に沿ったクリック遷移で、今後開催予定の開催日一覧を収集する。
    /// </para>
    /// </summary>
    public static IServiceCollection AddJraRaceScheduleCollectionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<JraRaceScheduleCollectionWorkflow>();
        return services;
    }

    /// <summary>
    /// <see cref="JraRaceCardCollectionWorkflow"/>、<see cref="JraRaceCardUrlDiscoveryAgent"/>、
    /// および <see cref="JraRaceCardScraper"/> を DI コンテナに登録する。
    /// <para>
    /// このワークフローは AI が出馬表 URL を発見し、
    /// Playwright が各ページをスクレイプして DB へ保存するという構成になっており、
    /// AI によるページ読み取りを最小限に抑えてトークン消費を削減する。
    /// </para>
    /// <para>
    /// 使用例（Program.cs または テスト初期化）:
    /// <code>
    /// builder.Services.AddHorseRacingAgentDomainSupport(connectionString);
    /// builder.Services.AddWebBrowserAgent();
    /// builder.Services.AddJraRaceCardCollectionWorkflow();
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddJraRaceCardCollectionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<JraRaceCardCollectionWorkflow>(sp =>
        {
            var browser = sp.GetRequiredService<IWebBrowser>();

            return new JraRaceCardCollectionWorkflow(
                browser,
                sp.GetRequiredService<JraRaceCardScraper>(),
                sp.GetRequiredService<DataCollectionWriteTools>());
        });
        return services;
    }

    /// <summary>
    /// <see cref="PredictionWorkflow"/> および 3 つの予測エージェントを
    /// DI コンテナに登録する。
    /// </summary>
    public static IServiceCollection AddPredictionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<RaceContextAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new RaceContextAgent(chatClient, CreateRaceQueryAndWebFetchTools(sp));
        });
        services.AddTransient<HorseAnalysisAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new HorseAnalysisAgent(chatClient, CreateRaceQueryAndWebFetchTools(sp));
        });
        services.AddTransient<PredictionAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new PredictionAgent(chatClient, CreatePredictionTools(sp));
        });
        services.AddTransient<PredictionWorkflow>(sp =>
            new PredictionWorkflow(
                sp.CreateRaceContextChatAgent(),
                sp.CreateHorseAnalysisChatAgent(),
                sp.CreatePredictionChatAgent()));
        return services;
    }

    public static ChatClientAgent CreateWebBrowserChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        var browser = services.GetRequiredService<IWebBrowser>();
        var options = services.GetRequiredService<IOptions<WebFetchOptions>>();
        var extractionAgent = services.GetService<PageDataExtractionAgent>();
        var logger = services.GetRequiredService<ILogger<PlaywrightTools>>();
        var playwrightTools = new PlaywrightTools(browser, options, extractionAgent, logger);

        return new ChatClientAgent(
            chatClient,
            name: name ?? WebBrowserAgent.AgentName,
            instructions: WebBrowserAgent.SystemPrompt,
            tools: playwrightTools.GetAITools());
    }

    public static ChatClientAgent CreateJraNavigationChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? JraNavigationAgent.AgentName,
            instructions: JraNavigationAgent.SystemPrompt,
            tools: services.GetRequiredService<JraPageExtractionTools>().GetAITools());
    }

    public static ChatClientAgent CreateRaceContextChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? RaceContextAgent.AgentName,
            instructions: RaceContextAgent.SystemPrompt,
            tools: CreateRaceQueryAndWebFetchTools(services));
    }

    public static ChatClientAgent CreateHorseAnalysisChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? HorseAnalysisAgent.AgentName,
            instructions: HorseAnalysisAgent.SystemPrompt,
            tools: CreateRaceQueryAndWebFetchTools(services));
    }

    public static ChatClientAgent CreatePredictionChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? PredictionAgent.AgentName,
            instructions: PredictionAgent.SystemPrompt,
            tools: CreatePredictionTools(services));
    }

    private static List<AITool> CreateRaceQueryAndWebFetchTools(IServiceProvider services)
    {
        var tools = new List<AITool>(services.GetRequiredService<RaceQueryTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<WebFetchTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<JraPageExtractionTools>().GetAITools());
        return tools;
    }

    private static List<AITool> CreatePredictionTools(IServiceProvider services)
    {
        var tools = new List<AITool>(services.GetRequiredService<RaceQueryTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<PredictionWriteTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<WebFetchTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<JraPageExtractionTools>().GetAITools());
        return tools;
    }
}


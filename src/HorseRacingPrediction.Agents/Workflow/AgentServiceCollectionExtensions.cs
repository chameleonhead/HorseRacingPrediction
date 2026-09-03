using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Agents.Plugins;
// JRAサイト再設計（docs/jra-scraping.md）により、旧 Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、以下の using を一時的に無効化する。
// using HorseRacingPrediction.Scraping.Scrapers.Jra;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // JRAサイト再設計により JraPageExtractionTools/JraRaceCardScraper は一時的に無効化中。
        // services.AddSingleton<JraPageExtractionTools>();
        // services.AddTransient<JraRaceCardScraper>(sp =>
        // {
        //     var browser = sp.GetRequiredService<IWebBrowser>();
        //     return new JraRaceCardScraper(browser);
        // });
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
    /// <see cref="PredictionWorkflow"/> および 3 つの予測エージェントを
    /// DI コンテナに登録する。
    /// </summary>
    public static IServiceCollection AddPredictionWorkflow(this IServiceCollection services)
    {
        services.AddTransient<RaceQueryTools>();
        services.AddTransient<PredictionWriteTools>();
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

    // JRAサイト再設計により JraPageExtractionTools が一時的に無効化されているため、このメソッドも一時的に無効化する。
    // public static ChatClientAgent CreateJraNavigationChatAgent(this IServiceProvider services, string? name = null)
    // {
    //     var chatClient = services.GetRequiredService<IChatClient>();
    //     return new ChatClientAgent(
    //         chatClient,
    //         name: name ?? JraNavigationAgent.AgentName,
    //         instructions: JraNavigationAgent.SystemPrompt,
    //         tools: services.GetRequiredService<JraPageExtractionTools>().GetAITools());
    // }

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

    /// <summary>
    /// <see cref="PostGenerationWorkflow"/> と、その3並行草稿エージェント
    /// (<see cref="HonmeiCommentaryAgent"/> / <see cref="AnaCommentaryAgent"/> / <see cref="DataRationaleAgent"/>) と
    /// 統合エージェント (<see cref="StoryPostComposerAgent"/>) を DI コンテナに登録する。
    /// </summary>
    public static IServiceCollection AddPostGenerationWorkflow(this IServiceCollection services)
    {
        services.TryAddTransient<RaceQueryTools>();

        services.AddTransient<HonmeiCommentaryAgent>(sp =>
            new HonmeiCommentaryAgent(sp.GetRequiredService<IChatClient>(), CreateRaceQueryOnlyTools(sp)));
        services.AddTransient<AnaCommentaryAgent>(sp =>
            new AnaCommentaryAgent(sp.GetRequiredService<IChatClient>(), CreateRaceQueryOnlyTools(sp)));
        services.AddTransient<DataRationaleAgent>(sp =>
            new DataRationaleAgent(sp.GetRequiredService<IChatClient>(), CreateRaceQueryOnlyTools(sp)));
        services.AddTransient<StoryPostComposerAgent>(sp =>
            new StoryPostComposerAgent(sp.GetRequiredService<IChatClient>(), CreateRaceQueryOnlyTools(sp)));
        services.AddTransient<PostGenerationWorkflow>();

        return services;
    }

    private static List<AITool> CreateRaceQueryOnlyTools(IServiceProvider services) =>
        new(services.GetRequiredService<RaceQueryTools>().GetAITools());

    private static List<AITool> CreateRaceQueryAndWebFetchTools(IServiceProvider services)
    {
        var tools = new List<AITool>(services.GetRequiredService<RaceQueryTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<WebFetchTools>().GetAITools());
        // JRAサイト再設計により JraPageExtractionTools は一時的に無効化中。
        // tools.AddRange(services.GetRequiredService<JraPageExtractionTools>().GetAITools());
        return tools;
    }

    private static List<AITool> CreatePredictionTools(IServiceProvider services)
    {
        var tools = new List<AITool>(services.GetRequiredService<RaceQueryTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<PredictionWriteTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<WebFetchTools>().GetAITools());
        // JRAサイト再設計により JraPageExtractionTools は一時的に無効化中。
        // tools.AddRange(services.GetRequiredService<JraPageExtractionTools>().GetAITools());
        return tools;
    }
}


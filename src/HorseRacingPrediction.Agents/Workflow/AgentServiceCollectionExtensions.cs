using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure;
using EventFlow.Extensions;
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
    public static IServiceCollection AddHorseRacingAgentDomainSupport(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSqliteDbContextProvider(connectionString);

        services.AddSingleton<HorseWeightHistoryLocator>();
        services.AddSingleton<PredictionComparisonViewLocator>();
        services.AddSingleton<MemoBySubjectLocator>();

        services.AddEventFlow(options =>
        {
            options
                .AddDefaults(typeof(RaceAggregate).Assembly)
                .AddDefaults(typeof(CreateRaceCommand).Assembly)
                .UseEntityFrameworkSqliteEventStore(connectionString)
                .UseInMemoryReadStoreFor<HorseReadModel>()
                .UseInMemoryReadStoreFor<JockeyReadModel>()
                .UseInMemoryReadStoreFor<TrainerReadModel>()
                .UseInMemoryReadStoreFor<RacePredictionContextReadModel>()
                .UseInMemoryReadStoreFor<RaceResultViewReadModel>()
                .UseInMemoryReadStoreFor<PredictionTicketReadModel>()
                .UseInMemoryReadStoreFor<HorseWeightHistoryReadModel, HorseWeightHistoryLocator>()
                .UseInMemoryReadStoreFor<PredictionComparisonViewReadModel, PredictionComparisonViewLocator>()
                .UseInMemoryReadStoreFor<MemoBySubjectReadModel, MemoBySubjectLocator>();
        });

        services.AddTransient<RaceQueryTools>();
        services.AddTransient<PredictionWriteTools>();
        services.AddTransient<DataCollectionWriteTools>();
        return services;
    }

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
            return new RaceDataAgent(chatClient, CreateDataCollectionTools(sp));
        });
        services.AddTransient<HorseDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new HorseDataAgent(chatClient, CreateDataCollectionTools(sp));
        });
        services.AddTransient<JockeyDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new JockeyDataAgent(chatClient, CreateDataCollectionTools(sp));
        });
        services.AddTransient<StableDataAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new StableDataAgent(chatClient, CreateDataCollectionTools(sp));
        });
        services.AddTransient<DataCollectionWorkflow>();
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
        services.AddTransient<WeekendRaceDiscoveryAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new WeekendRaceDiscoveryAgent(chatClient, CreateWeekendRaceDiscoveryTools(sp));
        });
        services.AddTransient<PostPositionPredictionAgent>(sp =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();
            return new PostPositionPredictionAgent(chatClient, CreatePostPositionPredictionTools(sp));
        });
        services.AddTransient<WeeklyScheduleWorkflow>(sp =>
        {
            return new WeeklyScheduleWorkflow(
                sp.GetRequiredService<WeekendRaceDiscoveryAgent>(),
                sp.GetRequiredService<DataCollectionWorkflow>(),
                sp.GetRequiredService<PostPositionPredictionAgent>());
        });
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

    public static ChatClientAgent CreateWeekendRaceDiscoveryChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? WeekendRaceDiscoveryAgent.AgentName,
            instructions: WeekendRaceDiscoveryAgent.SystemPrompt,
            tools: CreateWeekendRaceDiscoveryTools(services));
    }

    public static ChatClientAgent CreateRaceDataChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? RaceDataAgent.AgentName,
            instructions: RaceDataAgent.SystemPrompt,
            tools: CreateDataCollectionTools(services));
    }

    public static ChatClientAgent CreateHorseDataChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? HorseDataAgent.AgentName,
            instructions: HorseDataAgent.SystemPrompt,
            tools: CreateDataCollectionTools(services));
    }

    public static ChatClientAgent CreateJockeyDataChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? JockeyDataAgent.AgentName,
            instructions: JockeyDataAgent.SystemPrompt,
            tools: CreateDataCollectionTools(services));
    }

    public static ChatClientAgent CreateStableDataChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? StableDataAgent.AgentName,
            instructions: StableDataAgent.SystemPrompt,
            tools: CreateDataCollectionTools(services));
    }

    public static ChatClientAgent CreatePostPositionPredictionChatAgent(this IServiceProvider services, string? name = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        return new ChatClientAgent(
            chatClient,
            name: name ?? PostPositionPredictionAgent.AgentName,
            instructions: PostPositionPredictionAgent.SystemPrompt,
            tools: CreatePostPositionPredictionTools(services));
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

    private static List<AITool> CreateWeekendRaceDiscoveryTools(IServiceProvider services)
    {
        var webBrowserAgent = services.GetRequiredService<WebBrowserAgent>();
        var calendarTools = new CalendarTools();
        var tools = new List<AITool> { webBrowserAgent.CreateAIFunction() };
        tools.AddRange(calendarTools.GetAITools());
        return tools;
    }

    private static List<AITool> CreateDataCollectionTools(IServiceProvider services)
    {
        var webBrowserAgent = services.GetRequiredService<WebBrowserAgent>();
        var horseRacingTools = services.GetRequiredService<HorseRacingTools>();
        var writeTools = services.GetRequiredService<DataCollectionWriteTools>();
        var tools = new List<AITool>(horseRacingTools.GetAITools())
        {
            webBrowserAgent.CreateAIFunction()
        };
        tools.AddRange(writeTools.GetAITools());
        return tools;
    }

    private static List<AITool> CreatePostPositionPredictionTools(IServiceProvider services)
    {
        var webBrowserAgent = services.GetRequiredService<WebBrowserAgent>();
        return [webBrowserAgent.CreateAIFunction()];
    }

    private static List<AITool> CreateRaceQueryAndWebFetchTools(IServiceProvider services)
    {
        var tools = new List<AITool>(services.GetRequiredService<RaceQueryTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<WebFetchTools>().GetAITools());
        return tools;
    }

    private static List<AITool> CreatePredictionTools(IServiceProvider services)
    {
        var tools = new List<AITool>(services.GetRequiredService<RaceQueryTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<PredictionWriteTools>().GetAITools());
        tools.AddRange(services.GetRequiredService<WebFetchTools>().GetAITools());
        return tools;
    }
}


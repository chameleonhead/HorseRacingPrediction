using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.AgentClient.JraTesting;
using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.AgentClient.Web.ApiBrowsing;
using HorseRacingPrediction.AgentClient.Web.Components;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Scraping.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

// -------------------------------------------------------------------
// IChatClient — OpenAI または LMStudio を切り替え可能
// -------------------------------------------------------------------
var openAIApiKey = builder.Configuration["OpenAI:ApiKey"];
var lmStudioBaseUrl = builder.Configuration["LMStudio:BaseUrl"] ?? "http://localhost:1234";

if (string.IsNullOrWhiteSpace(openAIApiKey))
{
    var lmStudioModel = builder.Configuration["LMStudio:Model"] ?? "default";
    builder.Services.AddSingleton<IChatClient>(
        new LMStudioChatClient(new LMStudioChatClientOptions
        {
            BaseUri = new Uri(lmStudioBaseUrl),
            DefaultModel = lmStudioModel
        }));
}
else
{
    var apiKey = openAIApiKey
        ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException(
            "OpenAI API キーが設定されていません。" +
            "appsettings.json の \"OpenAI:ApiKey\" または " +
            "環境変数 OPENAI_API_KEY を設定してください。");

    var model = builder.Configuration["OpenAI:Model"] ?? "gpt-4o";

    builder.Services.AddSingleton<IChatClient>(
        new OpenAIClient(apiKey)
            .GetChatClient(model)
            .AsIChatClient());
}

// -------------------------------------------------------------------
// クラウド API への HTTP 接続設定
// -------------------------------------------------------------------
builder.Services.Configure<ApiClientOptions>(
    builder.Configuration.GetSection(ApiClientOptions.SectionName));
builder.Services.AddSingleton<IValidateOptions<ApiClientOptions>, ApiClientOptionsValidator>();
builder.Services.AddOptions<ApiClientOptions>()
    .Bind(builder.Configuration.GetSection(ApiClientOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddHttpAgentServices();

// -------------------------------------------------------------------
// WebBrowser + WebFetchTools（Playwright）
// -------------------------------------------------------------------
builder.Services.AddSingleton<IWebBrowserSessionFactory, PlaywrightWebBrowserSessionFactory>();
builder.Services.AddSingleton<IWebBrowser>(sp =>
    sp.GetRequiredService<IWebBrowserSessionFactory>().CreateAsync().GetAwaiter().GetResult());
builder.Services.Configure<WebFetchOptions>(
    builder.Configuration.GetSection(WebFetchOptions.SectionName));
builder.Services.AddWebBrowserAgent();
builder.Services.AddPredictionWorkflow();
builder.Services.AddJraRaceCardCollectionWorkflow();
builder.Services.AddJraRaceResultCollectionWorkflow();
builder.Services.AddJraRaceScheduleCollectionWorkflow();

// -------------------------------------------------------------------
// エージェントを DevUI に登録
// -------------------------------------------------------------------

// WebBrowserAgent（汎用 Web 情報取得）
builder.AddAIAgent(
    WebBrowserAgent.AgentName,
    (sp, name) => sp.CreateWebBrowserChatAgent(name));

builder.AddAIAgent(
    JraNavigationAgent.AgentName,
    (sp, name) => sp.CreateJraNavigationChatAgent(name));

builder.AddAIAgent(
    RaceContextAgent.AgentName,
    (sp, name) => sp.CreateRaceContextChatAgent(name));

builder.AddAIAgent(
    HorseAnalysisAgent.AgentName,
    (sp, name) => sp.CreateHorseAnalysisChatAgent(name));

builder.AddAIAgent(
    PredictionAgent.AgentName,
    (sp, name) => sp.CreatePredictionChatAgent(name));

// -------------------------------------------------------------------
// ワークフローを DevUI に登録
// -------------------------------------------------------------------

// PredictionWorkflow: レースコンテキスト収集 → 馬分析 → 予測票作成 の順次ワークフロー
builder.AddWorkflow(
    "PredictionWorkflow",
    (sp, workflowName) =>
    {
        var raceContextAgent = sp.CreateRaceContextChatAgent();
        var horseAnalysisAgent = sp.CreateHorseAnalysisChatAgent();
        var predictionAgent = sp.CreatePredictionChatAgent();

        return AgentWorkflowBuilder.BuildSequential(
            workflowName,
            [raceContextAgent, horseAnalysisAgent, predictionAgent]);
    }).AddAsAIAgent();

// -------------------------------------------------------------------
// 収集登録処理 / 予想処理（分離）
// -------------------------------------------------------------------
builder.Services.Configure<AgentProcessingOptions>(
    builder.Configuration.GetSection(AgentProcessingOptions.SectionName));
builder.Services.AddSingleton<CollectionExecutionTrigger>();
builder.Services.AddSingleton<ProcessingStateStore>();
builder.Services.AddSingleton<JraResultDateParser>();
builder.Services.AddSingleton<IJraResultDateDiscoveryService, JraResultMonthDateDiscoveryService>();
builder.Services.AddSingleton<IHistoricalRaceReferenceCollector, JraHistoricalRaceReferenceCollector>();
builder.Services.AddSingleton<IJraRaceResultLookup, JraSiteDataCollectorRaceResultLookup>();
builder.Services.AddSingleton<IHistoricalRaceResultCollector, JraHistoricalRaceResultCollector>();
builder.Services.AddSingleton<IJraProfileLookup, JraSiteDataCollectorProfileLookup>();
builder.Services.AddSingleton<IHistoricalDataRequestHandler, JraHistoricalDataRequestHandler>();
builder.Services.AddSingleton<JraJsonExtractionService>();
builder.Services.AddTransient<HistoricalDataRequestPlanner>();
builder.Services.AddTransient<HistoricalDataRequestTracker>();
builder.Services.AddTransient<RaceTextInsightCollector>();
builder.Services.AddHostedService<ScrapingRegistrationService>();
builder.Services.AddHostedService<CollectionExecutionService>();
builder.Services.AddHostedService<HistoricalDataRequestExecutionService>();
builder.Services.AddHostedService<PredictionExecutionService>();

// -------------------------------------------------------------------
// OpenAI Responses / Conversations エンドポイント（DevUI 必須）
// -------------------------------------------------------------------
builder.AddOpenAIResponses();
builder.AddOpenAIConversations();

// -------------------------------------------------------------------
// Web UI（Blazor Server）・API 参照用 HttpClient
// -------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient<ApiBrowseClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<ApiClientOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        client.BaseAddress = new Uri(options.BaseUrl);
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
});

// -------------------------------------------------------------------
// DevUI（開発時のみ）
// -------------------------------------------------------------------
if (builder.Environment.IsDevelopment())
{
    builder.AddDevUI();
}

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapOpenAIResponses();
app.MapOpenAIConversations();
app.MapAgentCollectionStatusEndpoints();
app.MapAgentAcquisitionStatusEndpoints();
app.MapAgentDashboardEndpoints();
app.MapJraJsonTesterEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.Run();

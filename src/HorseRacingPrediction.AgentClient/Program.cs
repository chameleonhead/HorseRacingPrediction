using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Workflow;
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
builder.Services.AddHttpAgentServices();

// -------------------------------------------------------------------
// WebBrowser + WebFetchTools（Playwright）
// -------------------------------------------------------------------
builder.Services.AddSingleton<IWebBrowser>(sp =>
    PlaywrightWebBrowser.CreateAsync().GetAwaiter().GetResult());
builder.Services.Configure<WebFetchOptions>(
    builder.Configuration.GetSection(WebFetchOptions.SectionName));
builder.Services.AddWebBrowserAgent();
builder.Services.AddPredictionWorkflow();
builder.Services.AddDataCollectionWorkflow();
builder.Services.AddJraRaceCardCollectionWorkflow();
builder.Services.AddJraRaceResultCollectionWorkflow();

// -------------------------------------------------------------------
// エージェントを DevUI に登録
// -------------------------------------------------------------------

// WebBrowserAgent（汎用 Web 情報取得）
builder.AddAIAgent(
    WebBrowserAgent.AgentName,
    (sp, name) => sp.CreateWebBrowserChatAgent(name));

// レース情報収集エージェント
builder.AddAIAgent(
    RaceDataAgent.AgentName,
    (sp, name) => sp.CreateRaceDataChatAgent(name));

// 馬情報収集エージェント
builder.AddAIAgent(
    HorseDataAgent.AgentName,
    (sp, name) => sp.CreateHorseDataChatAgent(name));

// 騎手情報収集エージェント
builder.AddAIAgent(
    JockeyDataAgent.AgentName,
    (sp, name) => sp.CreateJockeyDataChatAgent(name));

// 厩舎（調教師）情報収集エージェント
builder.AddAIAgent(
    StableDataAgent.AgentName,
    (sp, name) => sp.CreateStableDataChatAgent(name));

// 枠順確定後予測エージェント（金曜フェーズ）
builder.AddAIAgent(
    PostPositionPredictionAgent.AgentName,
    (sp, name) => sp.CreatePostPositionPredictionChatAgent(name));

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

// DataCollectionWorkflow: レース・馬・騎手・厩舎データを並列収集するワークフロー
builder.AddWorkflow(
    "DataCollectionWorkflow",
    (sp, workflowName) =>
    {
        var raceDataAgent = sp.CreateRaceDataChatAgent();
        var horseDataAgent = sp.CreateHorseDataChatAgent();
        var jockeyDataAgent = sp.CreateJockeyDataChatAgent();
        var stableDataAgent = sp.CreateStableDataChatAgent();

        return AgentWorkflowBuilder.BuildConcurrent(
            workflowName,
            [raceDataAgent, horseDataAgent, jockeyDataAgent, stableDataAgent],
            aggregator: null);
    }).AddAsAIAgent();

// -------------------------------------------------------------------
// 収集登録処理 / 予想処理（分離）
// -------------------------------------------------------------------
builder.Services.Configure<AgentProcessingOptions>(
    builder.Configuration.GetSection(AgentProcessingOptions.SectionName));
builder.Services.AddSingleton<ProcessingStateStore>();
builder.Services.AddTransient<RaceTextInsightCollector>();
builder.Services.AddHostedService<ScrapingRegistrationService>();
builder.Services.AddHostedService<PredictionExecutionService>();

// -------------------------------------------------------------------
// OpenAI Responses / Conversations エンドポイント（DevUI 必須）
// -------------------------------------------------------------------
builder.AddOpenAIResponses();
builder.AddOpenAIConversations();

// -------------------------------------------------------------------
// DevUI（開発時のみ）
// -------------------------------------------------------------------
if (builder.Environment.IsDevelopment())
{
    builder.AddDevUI();
}

var app = builder.Build();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (app.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.Run();

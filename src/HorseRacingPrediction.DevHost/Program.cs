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

// // -------------------------------------------------------------------
// // OpenAI IChatClient
// // -------------------------------------------------------------------
// var openAIApiKey = builder.Configuration["OpenAI:ApiKey"]
//     ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
//     ?? throw new InvalidOperationException(
//         "OpenAI API キーが設定されていません。" +
//         "appsettings.Development.json の \"OpenAI:ApiKey\" または " +
//         "環境変数 OPENAI_API_KEY を設定してください。");

// var openAIModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4o";

// builder.Services.AddSingleton<IChatClient>(
//     new OpenAIClient(openAIApiKey)
//         .GetChatClient(openAIModel)
//         .AsIChatClient());
builder.Services.AddSingleton<IChatClient>(
    new LMStudioChatClient(new LMStudioChatClientOptions()
    {
        BaseUri = new Uri("http://127.0.0.1:1234"),
        DefaultModel = "google/gemma-3n-e4b",
    }));
builder.Services.AddSingleton(sp =>
    new PageDataExtractionAgent(sp.GetRequiredService<IChatClient>()));

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Data Source=eventstore-devui.db";

builder.Services.AddHorseRacingAgentDomainSupport(connectionString);

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
builder.Services.AddWeeklyScheduleWorkflow();

// -------------------------------------------------------------------
// 競馬予測エージェントを DevUI に登録
// -------------------------------------------------------------------

// WebBrowserAgent（汎用 Web 情報取得）
builder.AddAIAgent(
    WebBrowserAgent.AgentName,
    (sp, name) => sp.CreateWebBrowserChatAgent(name));

// 週末レース発見エージェント（木曜フェーズ）
builder.AddAIAgent(
    WeekendRaceDiscoveryAgent.AgentName,
    (sp, name) => sp.CreateWeekendRaceDiscoveryChatAgent(name));

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

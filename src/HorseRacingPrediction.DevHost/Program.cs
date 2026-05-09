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

// -------------------------------------------------------------------
// 競馬予測エージェントを DevUI に登録
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

using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IChatClient>(
    new LMStudioChatClient(new LMStudioChatClientOptions()
    {
        BaseUri = new Uri("http://127.0.0.1:1234"),
        DefaultModel = "google/gemma-4-e2b",
    }));

// -------------------------------------------------------------------
// WebBrowser + WebFetchTools（Playwright）
// -------------------------------------------------------------------
builder.Services.AddSingleton<IWebBrowser>(await PlaywrightWebBrowser.CreateAsync());
builder.Services.Configure<WebFetchOptions>(
    builder.Configuration.GetSection(WebFetchOptions.SectionName));
builder.Services.AddTransient<WebFetchTools>();

// -------------------------------------------------------------------
// 競馬予測エージェントを DevUI に登録
// -------------------------------------------------------------------

// 週末レース発見エージェント（木曜フェーズ）
builder.AddAIAgent(
    WeekendRaceDiscoveryAgent.AgentName,
    (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
        return new ChatClientAgent(chatClient, name: name, instructions: WeekendRaceDiscoveryAgent.SystemPrompt, tools: tools);
    });

// レース情報収集エージェント
builder.AddAIAgent(
    RaceDataAgent.AgentName,
    (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
        return new ChatClientAgent(chatClient, name: name, instructions: RaceDataAgent.SystemPrompt, tools: tools);
    });

// 馬情報収集エージェント
builder.AddAIAgent(
    HorseDataAgent.AgentName,
    (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
        return new ChatClientAgent(chatClient, name: name, instructions: HorseDataAgent.SystemPrompt, tools: tools);
    });

// 騎手情報収集エージェント
builder.AddAIAgent(
    JockeyDataAgent.AgentName,
    (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
        return new ChatClientAgent(chatClient, name: name, instructions: JockeyDataAgent.SystemPrompt, tools: tools);
    });

// 厩舎（調教師）情報収集エージェント
builder.AddAIAgent(
    StableDataAgent.AgentName,
    (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
        return new ChatClientAgent(chatClient, name: name, instructions: StableDataAgent.SystemPrompt, tools: tools);
    });

// 枠順確定後予測エージェント（金曜フェーズ）
builder.AddAIAgent(
    PostPositionPredictionAgent.AgentName,
    (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();
        return new ChatClientAgent(chatClient, name: name, instructions: PostPositionPredictionAgent.SystemPrompt, tools: tools);
    });

// -------------------------------------------------------------------
// ワークフローを DevUI に登録
// -------------------------------------------------------------------

// PredictionWorkflow: レースコンテキスト収集 → 馬分析 → 予測票作成 の順次ワークフロー
builder.AddWorkflow(
    "PredictionWorkflow",
    (sp, workflowName) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();

        var raceContextAgent = new ChatClientAgent(
            chatClient,
            name: RaceContextAgent.AgentName,
            instructions: RaceContextAgent.SystemPrompt,
            tools: tools);
        var horseAnalysisAgent = new ChatClientAgent(
            chatClient,
            name: HorseAnalysisAgent.AgentName,
            instructions: HorseAnalysisAgent.SystemPrompt,
            tools: tools);
        var predictionAgent = new ChatClientAgent(
            chatClient,
            name: PredictionAgent.AgentName,
            instructions: PredictionAgent.SystemPrompt,
            tools: tools);

        return AgentWorkflowBuilder.BuildSequential(
            workflowName,
            [raceContextAgent, horseAnalysisAgent, predictionAgent]);
    }).AddAsAIAgent();

// DataCollectionWorkflow: レース・馬・騎手・厩舎データを並列収集するワークフロー
builder.AddWorkflow(
    "DataCollectionWorkflow",
    (sp, workflowName) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var tools = sp.GetRequiredService<WebFetchTools>().GetAITools();

        var raceDataAgent = new ChatClientAgent(
            chatClient,
            name: RaceDataAgent.AgentName,
            instructions: RaceDataAgent.SystemPrompt,
            tools: tools);
        var horseDataAgent = new ChatClientAgent(
            chatClient,
            name: HorseDataAgent.AgentName,
            instructions: HorseDataAgent.SystemPrompt,
            tools: tools);
        var jockeyDataAgent = new ChatClientAgent(
            chatClient,
            name: JockeyDataAgent.AgentName,
            instructions: JockeyDataAgent.SystemPrompt,
            tools: tools);
        var stableDataAgent = new ChatClientAgent(
            chatClient,
            name: StableDataAgent.AgentName,
            instructions: StableDataAgent.SystemPrompt,
            tools: tools);

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

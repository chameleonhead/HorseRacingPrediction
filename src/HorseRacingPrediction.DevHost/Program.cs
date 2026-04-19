using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

// -------------------------------------------------------------------
// OpenAI IChatClient
// -------------------------------------------------------------------
var openAIApiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException(
        "OpenAI API キーが設定されていません。" +
        "appsettings.Development.json の \"OpenAI:ApiKey\" または " +
        "環境変数 OPENAI_API_KEY を設定してください。");

var openAIModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4o";

builder.Services.AddSingleton<IChatClient>(
    new OpenAIClient(openAIApiKey)
        .GetChatClient(openAIModel)
        .AsIChatClient());

// -------------------------------------------------------------------
// WebBrowser + WebFetchTools（Playwright）
// -------------------------------------------------------------------
builder.Services.AddSingleton<IWebBrowser, PlaywrightWebBrowser>();
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

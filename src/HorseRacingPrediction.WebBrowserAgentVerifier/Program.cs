using System.Text;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;

var prompt = args.Length > 0
    ? string.Join(' ', args)
    : "JRAのサイト(https://www.jra.go.jp/)から2026年皐月賞のサイトの出馬表のURLを見つけ、出馬表の内容を抽出してください。";

var searchQuery = Environment.GetEnvironmentVariable("WEB_AGENT_QUERY") ?? "2026 皐月賞 出馬表";
var objective = Environment.GetEnvironmentVariable("WEB_AGENT_OBJECTIVE") ?? "出馬表を取得する";
var entryUrl = Environment.GetEnvironmentVariable("WEB_AGENT_ENTRY_URL") ?? "https://www.jra.go.jp/";

var baseUri = Environment.GetEnvironmentVariable("LMSTUDIO_BASEURI") ?? "http://127.0.0.1:1234";
var model = Environment.GetEnvironmentVariable("LMSTUDIO_MODEL") ?? "google/gemma-3n-e4b";

IChatClient chatClient = new LMStudioChatClient(new LMStudioChatClientOptions()
{
    BaseUri = new Uri(baseUri),
    DefaultModel = model,
});

using var loggerFactory = LoggerFactory.Create(logging =>
{
    logging
        .SetMinimumLevel(LogLevel.Information)
        .AddSimpleConsole(options =>
        {
            options.SingleLine = false;
            options.TimestampFormat = "HH:mm:ss ";
        });
});

var extractionAgent = new PageDataExtractionAgent(
    chatClient,
    loggerFactory.CreateLogger<PageDataExtractionAgent>());
await using var browser = await PlaywrightWebBrowser.CreateAsync();

var options = Options.Create(new WebFetchOptions
{
    AllowedDomains =
    [
        "www.jra.go.jp",
        "jra.jp",
        "db.netkeiba.com",
        "race.netkeiba.com",
        "www.bing.com"
    ],
    SearchBaseUrl = "https://www.bing.com/search?q=",
    SearchResultsToFetch = 3
});

var playwrightTools = new PlaywrightTools(
    browser,
    options,
    extractionAgent,
    loggerFactory.CreateLogger<PlaywrightTools>());
var webFetchTools = new WebFetchTools(new WebBrowserAgent(chatClient, playwrightTools.GetAITools()));
var agent = new WebBrowserAgent(chatClient, playwrightTools.GetAITools());

Console.WriteLine("=== WebBrowserAgent Verifier ===");
Console.WriteLine($"Model   : {model}");
Console.WriteLine($"Prompt  : {prompt}");
Console.WriteLine();

try
{
    var result = await agent.InvokeAsync(prompt);
    Console.WriteLine(result);
}
catch (Exception ex)
{
    Console.WriteLine($"Agent invocation failed: {ex.Message}");
}

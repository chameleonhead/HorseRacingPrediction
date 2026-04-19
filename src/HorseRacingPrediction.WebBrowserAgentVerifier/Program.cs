using System.Text;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;

var prompt = args.Length > 0
    ? string.Join(' ', args)
    : "2026年の皐月賞について、JRA公式サイトを優先して出走馬情報を調べてください。必要ならサイト内を追加探索し、確認できた事実だけを整理して返してください。";

var searchQuery = Environment.GetEnvironmentVariable("WEB_AGENT_QUERY") ?? "2026 皐月賞";
var objective = Environment.GetEnvironmentVariable("WEB_AGENT_OBJECTIVE") ?? "出走馬情報を確認する";
var entryUrl = Environment.GetEnvironmentVariable("WEB_AGENT_ENTRY_URL");

var baseUri = Environment.GetEnvironmentVariable("LMSTUDIO_BASEURI") ?? "http://127.0.0.1:1234";
var model = Environment.GetEnvironmentVariable("LMSTUDIO_MODEL") ?? "google/gemma-3n-e4b";

IChatClient chatClient = new LMStudioChatClient(new LMStudioChatClientOptions()
{
    BaseUri = new Uri(baseUri),
    DefaultModel = model,
});

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

var playwrightTools = new PlaywrightTools(browser, options);
var webFetchTools = new WebFetchTools(new WebBrowserAgent(chatClient, playwrightTools.GetAITools()));
var agent = new WebBrowserAgent(chatClient, playwrightTools.GetAITools());

Console.WriteLine("=== WebBrowserAgent Verifier ===");
Console.WriteLine($"Model   : {model}");
Console.WriteLine($"Prompt  : {prompt}");
Console.WriteLine();

Console.WriteLine("--- Tool-level SearchWeb ---");
var toolResult = await webFetchTools.SearchWeb(searchQuery, objective, "www.jra.go.jp");
Console.WriteLine(toolResult);
Console.WriteLine();

if (!string.IsNullOrWhiteSpace(entryUrl))
{
    Console.WriteLine("--- Tool-level ExploreFromEntryPoint ---");
    var exploreResult = await webFetchTools.ExploreFromEntryPoint(entryUrl, objective);
    Console.WriteLine(exploreResult);
    Console.WriteLine();
}

Console.WriteLine("--- Agent result ---");
var result = await agent.InvokeAsync(prompt);
Console.WriteLine(result);

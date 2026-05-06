using System.Globalization;
using System.Text;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;

var scenario = GetArgValue(args, "--scenario") ?? "monthly-jra-race-schedule";
var runDateArg = GetArgValue(args, "--run-date");
var allowAnyDay = HasArg(args, "--allow-any-day");
var maxWeekendsArg = GetArgValue(args, "--max-weekends");
var scrapeCards = HasArg(args, "--scrape-cards");
var maxScrapesArg = GetArgValue(args, "--max-scrapes");
var targetUrlArg = GetArgValue(args, "--url");
var extractionProfileArg = GetArgValue(args, "--extraction-profile");

var prompt = args.Length > 0
    ? string.Join(' ', args)
    : "JRAのサイト(https://www.jra.go.jp/)から今後のレース開催予定を調査してください。";

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
    loggerFactory.CreateLogger<PageDataExtractionAgent>(),
    modelId: model,
    profileOverride: extractionProfileArg);
var options = Options.Create(new WebFetchOptions
{
    AllowedDomains =
    [
        "www.jra.go.jp",
        "jra.jp",
        "duckduckgo.com"
    ],
    SearchBaseUrl = "https://duckduckgo.com/?q=",
    SearchResultsToFetch = 3
});
await using var browser = await PlaywrightWebBrowser.CreateAsync(searchBaseUrl: options.Value.SearchBaseUrl);

var playwrightTools = new PlaywrightTools(
    browser,
    options,
    extractionAgent,
    loggerFactory.CreateLogger<PlaywrightTools>());
var webFetchTools = new WebFetchTools(new WebBrowserAgent(chatClient, playwrightTools.GetAITools()));
var agent = new WebBrowserAgent(chatClient, playwrightTools.GetAITools());

Console.WriteLine("=== WebBrowserAgent Verifier ===");
Console.WriteLine($"Model   : {model}");
Console.WriteLine($"Scenario: {scenario}");
Console.WriteLine();

try
{
    if (string.Equals(scenario, "monthly-jra-race-schedule", StringComparison.OrdinalIgnoreCase))
    {
        var runDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (!allowAnyDay && runDate.Day is not 1 and not 15)
        {
            Console.WriteLine("このシナリオは原則として毎月1日または15日に実行します。");
            Console.WriteLine("検証目的で実行する場合は --allow-any-day を指定してください。");
            return;
        }

        var maxWeekends = maxWeekendsArg is null
            ? int.MaxValue
            : int.Parse(maxWeekendsArg, CultureInfo.InvariantCulture);
        var maxScrapes = maxScrapesArg is null
            ? 3
            : int.Parse(maxScrapesArg, CultureInfo.InvariantCulture);

        var weekends = BuildTargetWeekends(runDate)
            .Take(maxWeekends)
            .ToList();
        var targetDates = weekends
            .SelectMany(saturday => new[] { saturday, saturday.AddDays(1) })
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var discoveryAgent = new JraUrlDiscoveryAgent(chatClient, playwrightTools.GetAITools());
        var raceCardScraper = new JraRaceCardScraper(browser);

        Console.WriteLine($"RunDate : {runDate:yyyy-MM-dd}");
        Console.WriteLine($"Window  : {runDate:yyyy-MM-dd} - {GetEndOfNextMonth(runDate):yyyy-MM-dd}");
        Console.WriteLine($"Weekends: {string.Join(", ", weekends.Select(d => d.ToString("yyyy-MM-dd")))}");
        Console.WriteLine();

        var discoveredUrls = new List<JraRaceCardUrl>();

        foreach (var date in targetDates)
        {
            Console.WriteLine($"[JRA Discover] {date:yyyy-MM-dd} の出馬表URLを収集中...");
            var urls = await discoveryAgent.DiscoverUrlsAsync(date);
            Console.WriteLine($"[JRA Discover] {date:yyyy-MM-dd} -> {urls.Count} 件");
            discoveredUrls.AddRange(urls);
        }

        var distinctUrls = discoveredUrls
            .DistinctBy(u => u.Url)
            .OrderBy(u => u.RaceDate)
            .ThenBy(u => u.Racecourse)
            .ThenBy(u => u.RaceNumber)
            .ToList();

        var scheduleDates = distinctUrls
            .Where(u => u.RaceDate.HasValue)
            .Select(u => u.RaceDate!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("=== 予定日一覧 (JRA 出馬表URLより抽出) ===");
        if (scheduleDates.Count == 0)
        {
            Console.WriteLine("予定日は取得できませんでした。");
        }
        else
        {
            foreach (var d in scheduleDates)
            {
                Console.WriteLine($"- {d:yyyy-MM-dd} ({GetJapaneseDayOfWeek(d.DayOfWeek)})");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 出馬表URL一覧 (重複除外) ===");
        if (distinctUrls.Count == 0)
        {
            Console.WriteLine("出馬表URLは取得できませんでした。");
        }
        else
        {
            foreach (var item in distinctUrls)
            {
                Console.WriteLine($"- {item.RaceDate:yyyy-MM-dd} {item.Racecourse ?? item.RacecourseCode} {item.RaceNumber}R");
                Console.WriteLine($"  {item.Url}");
            }
        }

        if (scrapeCards && distinctUrls.Count > 0 && maxScrapes > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"=== 出馬表スクレイプ検証 (最大 {maxScrapes} 件) ===");

            foreach (var url in distinctUrls.Take(maxScrapes))
            {
                Console.WriteLine($"[Scrape] {url.Url}");
                var card = await raceCardScraper.ScrapeAsync(url.Url);

                if (card is null)
                {
                    Console.WriteLine("  -> 取得失敗");
                    continue;
                }

                Console.WriteLine($"  -> {card.RaceDate:yyyy-MM-dd} {card.Racecourse}{card.RaceNumber}R {card.RaceName}");
                Console.WriteLine($"     Entries: {card.Entries.Count}");
            }
        }
    }
    else if (string.Equals(scenario, "jra-graded-races", StringComparison.OrdinalIgnoreCase))
    {
        var url = targetUrlArg ?? "https://www.jra.go.jp/datafile/seiseki/replay/2026/jyusyo.html";
        var gradedRaceScraper = new JraGradedRaceListScraper(browser);

        Console.WriteLine($"Target  : {url}");
        Console.WriteLine("重賞レース一覧ページをスクレイプしています...");

        var result = await gradedRaceScraper.ScrapeAsync(url);
        if (result is null)
        {
            Console.WriteLine("取得に失敗しました。");
            return;
        }

        Console.WriteLine($"Year    : {result.Year}");
        Console.WriteLine($"Races   : {result.Races.Count}");
        Console.WriteLine();

        foreach (var race in result.Races)
        {
            Console.WriteLine($"- {race.RaceDate:yyyy-MM-dd} {race.Grade} {race.RaceName} ({race.Racecourse})");
            Console.WriteLine($"  条件: {race.Conditions ?? "-"} / コース: {race.Course ?? "-"}");
            Console.WriteLine($"  勝ち馬: {race.WinnerHorse ?? "-"} / 騎手: {race.WinnerJockey ?? "-"}");
            Console.WriteLine($"  結果URL: {race.ResultUrl ?? "-"}");
        }
    }
    else if (string.Equals(scenario, "monthly-race-discovery", StringComparison.OrdinalIgnoreCase))
    {
        var runDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (!allowAnyDay && runDate.Day is not 1 and not 15)
        {
            Console.WriteLine("このシナリオは原則として毎月1日または15日に実行します。");
            Console.WriteLine("検証目的で実行する場合は --allow-any-day を指定してください。");
            return;
        }

        var maxWeekends = maxWeekendsArg is null
            ? int.MaxValue
            : int.Parse(maxWeekendsArg, CultureInfo.InvariantCulture);

        var workflow = WeeklyScheduleWorkflow.Create(chatClient, agent);
        var targetWeekends = BuildTargetWeekends(runDate)
            .Take(maxWeekends)
            .ToList();

        Console.WriteLine($"RunDate : {runDate:yyyy-MM-dd}");
        Console.WriteLine($"Window  : {runDate:yyyy-MM-dd} - {GetEndOfNextMonth(runDate):yyyy-MM-dd}");
        Console.WriteLine($"Weekends: {string.Join(", ", targetWeekends.Select(d => d.ToString("yyyy-MM-dd")))}");
        Console.WriteLine();

        var allRaces = new List<WeekendRaceInfo>();

        foreach (var weekend in targetWeekends)
        {
            Console.WriteLine($"[Discover] {weekend:yyyy-MM-dd} 週のレースを収集中...");
            var races = await workflow.DiscoverRacesAsync(weekend);
            Console.WriteLine($"[Discover] {weekend:yyyy-MM-dd} -> {races.Count} 件");
            allRaces.AddRange(races);
        }

        var distinctRaces = allRaces
            .DistinctBy(r => $"{r.RaceDate:yyyy-MM-dd}|{r.Racecourse}|{r.RaceNumber}|{r.RaceName}")
            .OrderBy(r => r.RaceDate)
            .ThenBy(r => r.Racecourse)
            .ThenBy(r => r.RaceNumber)
            .ToList();

        var scheduleDates = distinctRaces
            .Select(r => r.RaceDate)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("=== 予定日一覧 (JRA) ===");
        if (scheduleDates.Count == 0)
        {
            Console.WriteLine("予定日は取得できませんでした。");
        }
        else
        {
            foreach (var d in scheduleDates)
            {
                Console.WriteLine($"- {d:yyyy-MM-dd} ({GetJapaneseDayOfWeek(d.DayOfWeek)})");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== レース一覧 (重複除外) ===");
        foreach (var race in distinctRaces)
        {
            Console.WriteLine($"- {race.RaceDate:yyyy-MM-dd} {race.Racecourse}{race.RaceNumber}R {race.RaceName}");
        }
    }
    else
    {
        Console.WriteLine($"Prompt  : {prompt}");
        var result = await agent.InvokeAsync(prompt);
        Console.WriteLine(result);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Agent invocation failed: {ex.Message}");
}

static IReadOnlyList<DateOnly> BuildTargetWeekends(DateOnly runDate)
{
    var end = GetEndOfNextMonth(runDate);
    var dates = new List<DateOnly>();

    for (var date = runDate; date <= end; date = date.AddDays(1))
    {
        if (date.DayOfWeek == DayOfWeek.Saturday)
        {
            dates.Add(date);
        }
    }

    return dates;
}

static DateOnly GetEndOfNextMonth(DateOnly baseDate)
{
    var nextMonth = baseDate.AddMonths(1);
    var firstDayOfFollowingMonth = new DateOnly(nextMonth.Year, nextMonth.Month, 1).AddMonths(1);
    return firstDayOfFollowingMonth.AddDays(-1);
}

static string GetJapaneseDayOfWeek(DayOfWeek dayOfWeek)
{
    return dayOfWeek switch
    {
        DayOfWeek.Sunday => "日",
        DayOfWeek.Monday => "月",
        DayOfWeek.Tuesday => "火",
        DayOfWeek.Wednesday => "水",
        DayOfWeek.Thursday => "木",
        DayOfWeek.Friday => "金",
        DayOfWeek.Saturday => "土",
        _ => dayOfWeek.ToString()
    };
}

static bool HasArg(string[] args, string key) =>
    args.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));

static string? GetArgValue(string[] args, string key)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

using System.Globalization;
using System.Text;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;

var scenario = GetArgValue(args, "--scenario") ?? "jra-task-agent";
var runDateArg = GetArgValue(args, "--run-date");
var allowAnyDay = HasArg(args, "--allow-any-day");
var maxWeekendsArg = GetArgValue(args, "--max-weekends");
var maxScrapesArg = GetArgValue(args, "--max-scrapes");
var maxEntriesArg = GetArgValue(args, "--max-entries");
var targetUrlArg = GetArgValue(args, "--url");
var extractionProfileArg = GetArgValue(args, "--extraction-profile");

var prompt = args.Length > 0
    ? string.Join(' ', args)
    : "JRAのサイト(https://www.jra.go.jp/)から今後のレース開催予定を調査してください。";

var baseUri = Environment.GetEnvironmentVariable("LMSTUDIO_BASEURI") ?? "http://127.0.0.1:1234";
var model = Environment.GetEnvironmentVariable("LMSTUDIO_MODEL") ?? "google/gemma-3n-e4b";

IChatClient chatClient = new LMStudioChatClient(new LMStudioChatClientOptions
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
var agent = new WebBrowserAgent(chatClient, playwrightTools.GetAITools());

Console.WriteLine("=== WebBrowserAgent Verifier ===");
Console.WriteLine($"Model   : {model}");
Console.WriteLine($"Scenario: {scenario}");
Console.WriteLine();

try
{
    if (string.Equals(scenario, "jra-task-agent", StringComparison.OrdinalIgnoreCase))
    {
        var runDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var racecourse = targetUrlArg ?? "東京";
        var raceNumber = maxScrapesArg is null
            ? 1
            : int.Parse(maxScrapesArg, CultureInfo.InvariantCulture);
        var maxEntries = maxEntriesArg is null
            ? 3
            : int.Parse(maxEntriesArg, CultureInfo.InvariantCulture);

        Console.WriteLine($"Date       : {runDate:yyyy-MM-dd}");
        Console.WriteLine($"Racecourse : {racecourse}");
        Console.WriteLine($"RaceNumber : {raceNumber}R");
        Console.WriteLine();

        await using var jraAgent = await JraTaskAgent.CreateAsync();

        Console.WriteLine("[1] 出馬表を取得中...");
        var cardResult = await jraAgent.RequestRaceCardAsync(runDate, racecourse, raceNumber);
        Console.WriteLine($"  Success : {cardResult.Success}");
        Console.WriteLine($"  PageKind: {cardResult.PageKind}");
        Console.WriteLine($"  URL     : {cardResult.SourceUrl}");
        Console.WriteLine($"  Steps   : {string.Join(" → ", cardResult.Trace.Steps)}");
        Console.WriteLine($"  Elapsed : {cardResult.Trace.Elapsed.TotalSeconds:F1}s");

        if (!cardResult.Success)
        {
            Console.WriteLine($"  Error   : {cardResult.Error}");
            return;
        }

        var raceCard = cardResult.Data;
        if (raceCard is null)
        {
            Console.WriteLine("  Error   : 出馬表データの型変換に失敗しました。");
            return;
        }

        Console.WriteLine($"  RaceName: {raceCard.RaceName ?? "-"}");
        Console.WriteLine($"  Entries : {raceCard.Entries.Count}");
        Console.WriteLine();

        Console.WriteLine("[2] オッズを取得中...");
        var oddsResult = await jraAgent.RequestOddsAsync(runDate, racecourse, raceNumber);
        Console.WriteLine($"  Success : {oddsResult.Success}");
        Console.WriteLine($"  PageKind: {oddsResult.PageKind}");
        Console.WriteLine($"  Steps   : {string.Join(" → ", oddsResult.Trace.Steps)}");
        if (oddsResult.Success)
        {
            var odds = oddsResult.Data;
            if (odds is not null)
            {
                Console.WriteLine($"  WinOdds : {odds.WinOdds.Count} 件");
                foreach (var o in odds.WinOdds.Take(5))
                {
                    Console.WriteLine($"    {o.HorseNumber}番 {o.HorseName ?? "-"} : {o.Odds?.ToString("F1") ?? "-"} ({o.Popularity}人気)");
                }
            }
        }
        else
        {
            Console.WriteLine($"  Error   : {oddsResult.Error}");
        }

        Console.WriteLine();
        Console.WriteLine($"[3] プロフィールを取得中（最大 {maxEntries} 頭）...");
        await jraAgent.RequestRaceCardAsync(runDate, racecourse, raceNumber);

        foreach (var entry in raceCard.Entries.Take(maxEntries))
        {
            if (string.IsNullOrWhiteSpace(entry.HorseName)) continue;
            Console.WriteLine($"  [{entry.HorseNumber}番] {entry.HorseName}");

            var horseResult = await jraAgent.RequestHorseProfileAsync(entry.HorseName);
            if (horseResult.Success)
            {
                var p = horseResult.Data;
                Console.WriteLine($"    Horse : 性別={p?.SexCode ?? "-"} 生年月日={p?.BirthDate?.ToString("yyyy-MM-dd") ?? "-"} 父={p?.SireName ?? "-"}");
                Console.WriteLine($"            馬主={p?.OwnerName ?? "-"} 生産={p?.BreederName ?? "-"}");
            }
            else
            {
                Console.WriteLine($"    Horse : 失敗 - {horseResult.Error}");
            }

            if (!string.IsNullOrWhiteSpace(entry.JockeyName))
            {
                var jockeyResult = await jraAgent.RequestJockeyProfileAsync(entry.JockeyName);
                if (jockeyResult.Success)
                {
                    var jp = jockeyResult.Data;
                    Console.WriteLine($"    Jockey: {entry.JockeyName} 所属={jp?.Affiliation ?? "-"} デビュー={jp?.DebutYear?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
                }
                else
                {
                    Console.WriteLine($"    Jockey: 失敗 - {jockeyResult.Error}");
                }
            }
            Console.WriteLine();
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
    else if (string.Equals(scenario, "monthly-jra-race-schedule", StringComparison.OrdinalIgnoreCase)
             || string.Equals(scenario, "jra-graded-races", StringComparison.OrdinalIgnoreCase)
             || string.Equals(scenario, "jra-race-results", StringComparison.OrdinalIgnoreCase)
             || string.Equals(scenario, "jra-race-results-eventflow-save", StringComparison.OrdinalIgnoreCase)
             || string.Equals(scenario, "jra-race-card-entry-details", StringComparison.OrdinalIgnoreCase)
             || string.Equals(scenario, "jra-profile-page-debug", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"シナリオ '{scenario}' は廃止されました。'jra-task-agent' を利用してください。");
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

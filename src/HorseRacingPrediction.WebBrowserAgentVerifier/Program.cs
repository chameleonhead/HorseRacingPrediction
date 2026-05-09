using System.Globalization;
using System.Text;
using EventFlow.Queries;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
var dbPathArg = GetArgValue(args, "--db-path");
var relationArg = GetArgValue(args, "--relation") ?? GetArgValue(args, "--label");

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
    else if (string.Equals(scenario, "jra-task-agent-schedule-dates", StringComparison.OrdinalIgnoreCase))
    {
        var runDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        Console.WriteLine($"ReferenceDate : {runDate:yyyy-MM-dd}");
        Console.WriteLine();

        await using var jraAgent = await JraTaskAgent.CreateAsync();

        Console.WriteLine("[1] 開催日一覧を取得中...");
        var scheduleResult = await jraAgent.RequestRaceScheduleDatesAsync(runDate);
        Console.WriteLine($"  Success : {scheduleResult.Success}");
        Console.WriteLine($"  URL     : {scheduleResult.SourceUrl}");
        Console.WriteLine($"  Steps   : {string.Join(" → ", scheduleResult.Trace.Steps)}");
        Console.WriteLine($"  Elapsed : {scheduleResult.Trace.Elapsed.TotalSeconds:F1}s");

        if (!scheduleResult.Success || scheduleResult.Data is null)
        {
            Console.WriteLine($"  Error   : {scheduleResult.Error ?? "開催日一覧を取得できませんでした。"}");
            return;
        }

        Console.WriteLine($"  Dates   : {scheduleResult.Data.RaceDates.Count}");
        foreach (var d in scheduleResult.Data.RaceDates.Take(40))
        {
            Console.WriteLine($"    - {d:yyyy-MM-dd} ({GetJapaneseDayOfWeek(d.DayOfWeek)})");
        }
    }
    else if (string.Equals(scenario, "jra-task-agent-eventflow-save", StringComparison.OrdinalIgnoreCase))
    {
        var runDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var racecourse = targetUrlArg ?? "東京";
        var raceNumber = maxScrapesArg is null
            ? 1
            : int.Parse(maxScrapesArg, CultureInfo.InvariantCulture);

        var dbPath = string.IsNullOrWhiteSpace(dbPathArg)
            ? Path.Combine(AppContext.BaseDirectory, "eventstore-verifier.db")
            : dbPathArg;
        var connectionString = $"Data Source={dbPath}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHorseRacingAgentDomainSupport(connectionString);

        using var serviceProvider = services.BuildServiceProvider();
        var writeTools = serviceProvider.GetRequiredService<DataCollectionWriteTools>();
        var queryProcessor = serviceProvider.GetRequiredService<IQueryProcessor>();

        Console.WriteLine($"Date       : {runDate:yyyy-MM-dd}");
        Console.WriteLine($"Racecourse : {racecourse}");
        Console.WriteLine($"RaceNumber : {raceNumber}R");
        Console.WriteLine($"DB         : {dbPath}");
        Console.WriteLine();

        await using var jraAgent = await JraTaskAgent.CreateAsync();

        Console.WriteLine("[1] 出馬表を取得中...");
        var cardResult = await jraAgent.RequestRaceCardAsync(runDate, racecourse, raceNumber);
        Console.WriteLine($"  Success : {cardResult.Success}");
        Console.WriteLine($"  PageKind: {cardResult.PageKind}");
        Console.WriteLine($"  URL     : {cardResult.SourceUrl}");
        Console.WriteLine($"  Steps   : {string.Join(" → ", cardResult.Trace.Steps)}");

        if (!cardResult.Success || cardResult.Data is null)
        {
            Console.WriteLine($"  Error   : {cardResult.Error ?? "出馬表データを取得できませんでした。"}");
            return;
        }

        var raceCard = cardResult.Data;
        var raceDate = raceCard.RaceDate ?? runDate;
        var resolvedRacecourse = string.IsNullOrWhiteSpace(raceCard.Racecourse) ? racecourse : raceCard.Racecourse;
        var resolvedRaceNumber = raceCard.RaceNumber ?? raceNumber;
        var raceName = string.IsNullOrWhiteSpace(raceCard.RaceName) ? $"R{resolvedRaceNumber}" : raceCard.RaceName;

        var raceId = await writeTools.UpsertRace(
            raceDate: raceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            racecourseCode: resolvedRacecourse!,
            raceNumber: resolvedRaceNumber,
            raceName: raceName,
            entryCount: raceCard.Entries.Count > 0 ? raceCard.Entries.Count : null,
            gradeCode: raceCard.Grade,
            surfaceCode: raceCard.CourseType,
            distanceMeters: raceCard.Distance);

        foreach (var entry in raceCard.Entries)
        {
            var (sexCode, age) = ParseSexAge(entry.SexAge);
            await writeTools.UpsertRaceEntry(
                raceId: raceId,
                horseNumber: entry.HorseNumber,
                horseName: entry.HorseName,
                jockeyName: entry.JockeyName,
                trainerName: entry.TrainerName,
                gateNumber: entry.GateNumber,
                assignedWeight: entry.Weight,
                sexCode: sexCode,
                age: age,
                declaredWeight: entry.BodyWeight,
                declaredWeightDiff: entry.BodyWeightDiff);
        }

        Console.WriteLine($"[2] EventFlow 登録完了: RaceId={raceId}");
        Console.WriteLine($"  Registered entries: {raceCard.Entries.Count}");

        Console.WriteLine();
        Console.WriteLine("[3] ReadModel 検証中...");
        var readModel = await queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RaceResultViewReadModel>(raceId),
            CancellationToken.None);

        if (readModel is null || string.IsNullOrWhiteSpace(readModel.RaceId))
        {
            Console.WriteLine("  NG: RaceResultViewReadModel が取得できませんでした。");
            return;
        }

        Console.WriteLine("  OK: ReadModel 取得成功");
        Console.WriteLine($"  RaceId       : {readModel.RaceId}");
        Console.WriteLine($"  RaceDate     : {readModel.RaceDate:yyyy-MM-dd}");
        Console.WriteLine($"  Racecourse   : {readModel.RacecourseCode}");
        Console.WriteLine($"  RaceNumber   : {readModel.RaceNumber}");
        Console.WriteLine($"  RaceName     : {readModel.RaceName}");
        Console.WriteLine($"  Status       : {readModel.Status}");
        Console.WriteLine($"  EntryResults : {readModel.EntryResults.Count}");
    }
    else if (string.Equals(scenario, "jra-structured-thisweek", StringComparison.OrdinalIgnoreCase))
    {
        var url = string.IsNullOrWhiteSpace(targetUrlArg)
            ? "https://www.jra.go.jp/keiba/thisweek/"
            : targetUrlArg;

        await using var jraAgent = await JraTaskAgent.CreateAsync();
        await jraAgent.NavigateAsync(url);
        var structured = await jraAgent.ExtractCurrentStructuredPageAsync();

        Console.WriteLine($"URL        : {url}");
        PrintStructuredPageEnvelope(structured);
    }
    else if (string.Equals(scenario, "jra-structured-g1", StringComparison.OrdinalIgnoreCase))
    {
        var url = string.IsNullOrWhiteSpace(targetUrlArg)
            ? "https://www.jra.go.jp/keiba/g1/nmc.html"
            : targetUrlArg;

        await using var jraAgent = await JraTaskAgent.CreateAsync();
        await jraAgent.NavigateAsync(url);
        var structured = await jraAgent.ExtractCurrentStructuredPageAsync();

        Console.WriteLine($"URL        : {url}");
        PrintStructuredPageEnvelope(structured);
    }
    else if (string.Equals(scenario, "jra-follow-structured-next-link", StringComparison.OrdinalIgnoreCase))
    {
        var url = string.IsNullOrWhiteSpace(targetUrlArg)
            ? "https://www.jra.go.jp/keiba/thisweek/"
            : targetUrlArg;
        var relationOrLabel = string.IsNullOrWhiteSpace(relationArg)
            ? JraStructuredLinkRelations.OpenRaceCard
            : relationArg;

        await using var jraAgent = await JraTaskAgent.CreateAsync();
        await jraAgent.NavigateAsync(url);
        var structured = await jraAgent.FollowStructuredNextLinkAsync(relationOrLabel);

        Console.WriteLine($"URL        : {url}");
        Console.WriteLine($"Relation   : {relationOrLabel}");
        PrintStructuredPageEnvelope(structured);
    }
    else if (string.Equals(scenario, "jra-race-result-collection-debug", StringComparison.OrdinalIgnoreCase))
    {
        var runDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var dbPath = string.IsNullOrWhiteSpace(dbPathArg)
            ? Path.Combine(AppContext.BaseDirectory, "result-collection-debug.db")
            : dbPathArg;
        var connectionString = $"Data Source={dbPath}";

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging
                .SetMinimumLevel(LogLevel.Information)
                .AddSimpleConsole(options =>
                {
                    options.SingleLine = false;
                    options.TimestampFormat = "HH:mm:ss ";
                });
        });
        services.AddHorseRacingAgentDomainSupport(connectionString);

        using var serviceProvider = services.BuildServiceProvider();
        var writeTools = serviceProvider.GetRequiredService<DataCollectionWriteTools>();

        var discoveryAgent = new JraResultUrlDiscoveryAgent(
            browser,
            loggerFactory.CreateLogger<JraResultUrlDiscoveryAgent>());
        var scraper = new JraRaceResultScraper(browser);
        var workflow = new JraRaceResultCollectionWorkflow(
            discoveryAgent,
            scraper,
            writeTools,
            loggerFactory.CreateLogger<JraRaceResultCollectionWorkflow>());

        Console.WriteLine($"Date       : {runDate:yyyy-MM-dd}");
        Console.WriteLine($"DB         : {dbPath}");
        Console.WriteLine();

        Console.WriteLine("[1] 成績URLを発見中（JraTaskAgent）...");
        var discoveredUrls = await DiscoverResultUrlsWithJraTaskAgentAsync(runDate);
        Console.WriteLine($"  Discovered: {discoveredUrls.Count}");
        foreach (var url in discoveredUrls.Take(10))
        {
            Console.WriteLine(
                $"  - {url.RaceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-"} {url.Racecourse ?? url.RacecourseCode ?? "-"} {url.RaceNumber?.ToString(CultureInfo.InvariantCulture) ?? "-"}R {url.Url}");
        }

        Console.WriteLine();
        Console.WriteLine("[2] 成績ページをスクレイプ中...");
        var scraped = await workflow.ScrapeAllAsync(discoveredUrls);
        Console.WriteLine($"  Scraped   : {scraped.Count}");
        foreach (var item in scraped.Take(5))
        {
            Console.WriteLine(
                $"  - {item.Source.Url} => {item.Data.RaceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-"} {item.Data.Racecourse ?? "-"} {item.Data.RaceNumber?.ToString(CultureInfo.InvariantCulture) ?? "-"}R Entries={item.Data.Entries.Count}");
        }

        Console.WriteLine();
        Console.WriteLine("[3] EventFlowへ保存中...");
        var (savedRaceIds, errors) = await workflow.SaveAllAsync(scraped);
        Console.WriteLine($"  Saved     : {savedRaceIds.Count}");
        Console.WriteLine($"  Errors    : {errors.Count}");
        foreach (var raceId in savedRaceIds.Take(10))
        {
            Console.WriteLine($"  - Saved RaceId: {raceId}");
        }

        if (errors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("[Errors]");
            foreach (var error in errors.Take(10))
            {
                Console.WriteLine($"  - {error}");
            }
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

static void PrintStructuredPageEnvelope(JraStructuredPageEnvelope envelope)
{
    Console.WriteLine($"Success    : {envelope.Success}");
    Console.WriteLine($"PageKind   : {envelope.PageKind}");
    Console.WriteLine($"SourceUrl  : {envelope.SourceUrl}");
    Console.WriteLine($"Confidence : {envelope.Confidence}");

    if (!string.IsNullOrWhiteSpace(envelope.Error))
    {
        Console.WriteLine($"Error      : {envelope.Error}");
    }

    if (envelope.Issues.Count > 0)
    {
        Console.WriteLine("Issues     :");
        foreach (var issue in envelope.Issues)
        {
            Console.WriteLine($"  - [{issue.Severity}] {issue.Code}: {issue.Message}");
        }
    }

    if (envelope.RecommendedNextLinks.Count > 0)
    {
        Console.WriteLine("NextLinks  :");
        foreach (var nextLink in envelope.RecommendedNextLinks)
        {
            Console.WriteLine($"  - {nextLink.Relation} | {nextLink.Label} | {nextLink.NavigationMode} | {nextLink.Url}");
        }
    }

    if (envelope.Data is JraThisWeekPage thisWeekPage)
    {
        Console.WriteLine("Featured   :");
        foreach (var race in thisWeekPage.FeaturedRaces)
        {
            Console.WriteLine($"  - {race.RaceDate:yyyy-MM-dd} {race.RaceName} {race.Grade} {race.Racecourse} {race.Distance}");
        }
    }
    else if (envelope.Data is JraGradeOneSpecialPage gradeOnePage)
    {
        Console.WriteLine($"RaceName   : {gradeOnePage.RaceName}");
        Console.WriteLine($"RaceDate   : {gradeOnePage.RaceDate:yyyy-MM-dd}");
        Console.WriteLine($"Racecourse : {gradeOnePage.Racecourse}");
        Console.WriteLine($"Distance   : {gradeOnePage.Distance}");
    }
    else if (envelope.Data is JraRaceCardPage raceCardPage)
    {
        Console.WriteLine($"RaceName   : {raceCardPage.RaceName}");
        Console.WriteLine($"RaceDate   : {raceCardPage.RaceDate:yyyy-MM-dd}");
        Console.WriteLine($"Racecourse : {raceCardPage.Racecourse}");
        Console.WriteLine($"Distance   : {raceCardPage.Distance}");
    }
}

static (string? sexCode, int? age) ParseSexAge(string? sexAge)
{
    if (string.IsNullOrWhiteSpace(sexAge))
    {
        return (null, null);
    }

    var trimmed = sexAge.Trim();
    var sexCode = trimmed.Length > 0 ? trimmed[0].ToString() : null;
    var digits = new string(trimmed.Skip(1).Where(char.IsDigit).ToArray());
    var age = int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAge)
        ? parsedAge
        : (int?)null;
    return (sexCode, age);
}

static async Task<IReadOnlyList<JraRaceResultUrl>> DiscoverResultUrlsWithJraTaskAgentAsync(
    DateOnly raceDate,
    CancellationToken cancellationToken = default)
{
    await using var jraAgent = await JraTaskAgent.CreateAsync(cancellationToken);

    await jraAgent.NavigateAsync("https://www.jra.go.jp/keiba/", cancellationToken);

    var visitedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var visitedTransitions = new HashSet<string>(StringComparer.Ordinal);
    var seenResultUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var results = new List<JraRaceResultUrl>();
    const int maxPageVisits = 20;
    const int maxDepth = 4;

    await ExploreByClickAsync(depth: 0);

    return results
        .OrderBy(r => r.RaceNumber ?? int.MaxValue)
        .ThenBy(r => r.Url, StringComparer.Ordinal)
        .ToList();

    async Task ExploreByClickAsync(int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > maxDepth || visitedPages.Count >= maxPageVisits)
        {
            return;
        }

        var snapshot = await jraAgent.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: cancellationToken);
        var currentUrl = snapshot.Url;

        if (!string.IsNullOrWhiteSpace(currentUrl)
            && !currentUrl.StartsWith("https://www.jra.go.jp/keiba", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentUrl))
        {
            visitedPages.Add(currentUrl);
        }

        CollectResultUrls(snapshot, raceDate, seenResultUrls, results);

        var clickCandidates = BuildResultClickCandidates(snapshot, raceDate).ToList();
        foreach (var clickText in clickCandidates)
        {
            var transitionKey = $"{currentUrl}|{NormalizeText(clickText)}";
            if (!visitedTransitions.Add(transitionKey))
            {
                continue;
            }

            var clicked = false;
            try
            {
                await jraAgent.FollowAsync(clickText, cancellationToken);
                clicked = true;

                var nextSnapshot = await jraAgent.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: cancellationToken);
                CollectResultUrls(nextSnapshot, raceDate, seenResultUrls, results);

                await ExploreByClickAsync(depth + 1);
            }
            catch
            {
                // クリック失敗は次候補へ進む。
            }
            finally
            {
                if (clicked)
                {
                    try { await jraAgent.BackAsync(cancellationToken); } catch { }
                }
            }
        }
    }
}

static void CollectResultUrls(
    PageSnapshot snapshot,
    DateOnly raceDate,
    HashSet<string> seenResultUrls,
    List<JraRaceResultUrl> results)
{
    foreach (var link in snapshot.Links)
    {
        var absoluteUrl = NormalizeAbsoluteUrl(link.Url, snapshot.Url);
        if (string.IsNullOrWhiteSpace(absoluteUrl))
        {
            continue;
        }

        if (!absoluteUrl.Contains("CNAME=pw01skd0203_", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!seenResultUrls.Add(absoluteUrl))
        {
            continue;
        }

        var parsed = JraRaceResultUrl.ParseFromUrl(absoluteUrl, racecourse: null);
        if (parsed.RaceDate is null || parsed.RaceDate == raceDate)
        {
            results.Add(parsed);
        }
    }
}

static IReadOnlyList<string> BuildResultClickCandidates(PageSnapshot snapshot, DateOnly raceDate)
{
    var dayText = $"{raceDate.Month}月{raceDate.Day}日";
    var monthText = $"{raceDate.Month}月";
    var yearText = raceDate.Year.ToString(CultureInfo.InvariantCulture);

    var candidates = snapshot.Actions.Select(a => a.Text)
        .Concat(snapshot.Links.Select(l => l.Title))
        .Where(text => !string.IsNullOrWhiteSpace(text))
        .Select(text => text!.Trim())
        .Distinct(StringComparer.Ordinal)
        .Select(text => new { Text = text, Score = ScoreClickCandidate(text, dayText, monthText, yearText) })
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .ThenBy(x => x.Text.Length)
        .Select(x => x.Text)
        .Take(15)
        .ToList();

    return candidates;
}

static int ScoreClickCandidate(string text, string dayText, string monthText, string yearText)
{
    var normalized = NormalizeText(text);
    var score = 0;

    if (normalized.Contains("レース結果", StringComparison.Ordinal)) score += 120;
    if (normalized.Contains("結果", StringComparison.Ordinal)) score += 80;
    if (normalized.Contains("払戻", StringComparison.Ordinal)) score += 60;
    if (normalized.Contains("成績", StringComparison.Ordinal)) score += 60;
    if (normalized.Contains("開催", StringComparison.Ordinal)) score += 40;
    if (normalized.Contains("今週", StringComparison.Ordinal)) score += 35;
    if (normalized.Contains(NormalizeText(dayText), StringComparison.Ordinal)) score += 50;
    if (normalized.Contains(NormalizeText(monthText), StringComparison.Ordinal)) score += 30;
    if (normalized.Contains(yearText, StringComparison.Ordinal)) score += 25;

    return score;
}

static string NormalizeText(string text)
    => text.Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace("　", string.Empty, StringComparison.Ordinal)
        .Trim();

static string? NormalizeAbsoluteUrl(string? candidate, string? baseUrl)
{
    if (string.IsNullOrWhiteSpace(candidate))
    {
        return null;
    }

    if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
    {
        return absolute.AbsoluteUri;
    }

    if (!string.IsNullOrWhiteSpace(baseUrl)
        && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
        && Uri.TryCreate(baseUri, candidate, out var resolved))
    {
        return resolved.AbsoluteUri;
    }

    return null;
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

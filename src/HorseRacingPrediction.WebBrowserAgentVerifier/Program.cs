using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using EventFlow.Queries;
using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Local.Workflow;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;

const string DefaultApiBaseUrl = "http://localhost:5177";

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
var apiBaseUrlArg = GetArgValue(args, "--api-base-url");
var apiKeyArg = GetArgValue(args, "--api-key");
var lookaheadDaysArg = GetArgValue(args, "--lookahead-days");
var raceNumberArg = GetArgValue(args, "--race-number") ?? maxScrapesArg;

var prompt = args.Length > 0
    ? string.Join(' ', args)
    : "JRAのサイト(https://www.jra.go.jp/)から今後のレース開催予定を調査してください。";

var baseUri = Environment.GetEnvironmentVariable("LMSTUDIO_BASEURI") ?? "http://127.0.0.1:1234";
var model = Environment.GetEnvironmentVariable("LMSTUDIO_MODEL") ?? "default";

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
        if (!await EnsureSupportedRaceCardScenarioDateAsync(jraAgent, runDate, scenario, CancellationToken.None))
        {
            return;
        }

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
        foreach (var day in (scheduleResult.Data.ScheduledDays ?? []).Take(40))
        {
            var racecourses = day.Racecourses.Count == 0
                ? "-"
                : string.Join(", ", day.Racecourses);
            Console.WriteLine($"    - {day.Date:yyyy-MM-dd} ({GetJapaneseDayOfWeek(day.Date.DayOfWeek)}) [{racecourses}]");
        }
    }
    else if (string.Equals(scenario, "jra-task-agent-nearest-race-card", StringComparison.OrdinalIgnoreCase))
    {
        var referenceDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var raceNumber = raceNumberArg is null
            ? 1
            : int.Parse(raceNumberArg, CultureInfo.InvariantCulture);

        Console.WriteLine($"ReferenceDate : {referenceDate:yyyy-MM-dd}");
        Console.WriteLine($"RaceNumber    : {raceNumber}R");
        Console.WriteLine();

        await using var jraAgent = await JraTaskAgent.CreateAsync();

        Console.WriteLine("[1] 開催日一覧を取得中...");
        var scheduleResult = await jraAgent.RequestRaceScheduleDatesAsync(referenceDate);
        Console.WriteLine($"  Success : {scheduleResult.Success}");
        Console.WriteLine($"  URL     : {scheduleResult.SourceUrl}");
        Console.WriteLine($"  Steps   : {string.Join(" → ", scheduleResult.Trace.Steps)}");
        Console.WriteLine($"  Elapsed : {scheduleResult.Trace.Elapsed.TotalSeconds:F1}s");

        if (!scheduleResult.Success || scheduleResult.Data?.ScheduledDays is null)
        {
            Console.WriteLine($"  Error   : {scheduleResult.Error ?? "開催日一覧を取得できませんでした。"}");
            return;
        }

        var nearestScheduledDay = scheduleResult.Data.ScheduledDays
            .Where(day => day.Date >= referenceDate && day.Racecourses.Count > 0)
            .OrderBy(day => day.Date)
            .FirstOrDefault();

        if (nearestScheduledDay is null)
        {
            Console.WriteLine("  Error   : 直近開催日と開催場を特定できませんでした。");
            return;
        }

        var racecourse = nearestScheduledDay.Racecourses[0];
        Console.WriteLine($"  Nearest : {nearestScheduledDay.Date:yyyy-MM-dd} ({GetJapaneseDayOfWeek(nearestScheduledDay.Date.DayOfWeek)})");
        Console.WriteLine($"  Course  : {racecourse}");
        Console.WriteLine();

        Console.WriteLine("[2] カレンダー導線のまま出馬表へ遷移中...");
        var manualSteps = new List<string>();

        try
        {
            await jraAgent.FollowAsync("出馬表");
            manualSteps.Add("click: 出馬表");
        }
        catch
        {
            await jraAgent.BackAsync();
            manualSteps.Add("back");
            await jraAgent.FollowAsync("出馬表");
            manualSteps.Add("click: 出馬表");
        }

        var holdingsSnapshot = await jraAgent.GetPageSnapshotAsync(maxLinks: 300);
        var directRaceLink = FindRaceLink(holdingsSnapshot, nearestScheduledDay.Date, racecourse, raceNumber);
        if (!string.IsNullOrWhiteSpace(directRaceLink))
        {
            await jraAgent.NavigateAsync(directRaceLink);
            manualSteps.Add($"navigate: {directRaceLink}");
        }
        else
        {
            var holdingLabel = FindHoldingLabel(holdingsSnapshot, racecourse);
            if (string.IsNullOrWhiteSpace(holdingLabel))
            {
                Console.WriteLine("  Error   : 開催選択画面から対象競馬場の開催ラベルを特定できませんでした。");
                return;
            }

            await jraAgent.FollowAsync(holdingLabel);
            manualSteps.Add($"click: {holdingLabel}");

            var clickedRaceNumber = await TryOpenRaceNumberAsync(jraAgent, raceNumber);
            if (clickedRaceNumber is null)
            {
                Console.WriteLine($"  Error   : {raceNumber}R への遷移に失敗しました。");
                return;
            }

            manualSteps.Add($"click: {clickedRaceNumber}");
        }

        var cardResult = (await jraAgent.ExtractCurrentPageAsync()).ToTyped<JraRaceCardData>();
        Console.WriteLine($"  Success : {cardResult.Success}");
        Console.WriteLine($"  PageKind: {cardResult.PageKind}");
        Console.WriteLine($"  URL     : {cardResult.SourceUrl}");
        Console.WriteLine($"  Steps   : {string.Join(" → ", manualSteps)}");

        if (!cardResult.Success || cardResult.Data is null)
        {
            Console.WriteLine($"  Error   : {cardResult.Error ?? "出馬表を取得できませんでした。"}");
            return;
        }

        Console.WriteLine($"  RaceDate : {cardResult.Data.RaceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? nearestScheduledDay.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"  Course   : {cardResult.Data.Racecourse ?? racecourse}");
        Console.WriteLine($"  RaceName : {cardResult.Data.RaceName ?? "-"}");
        Console.WriteLine($"  Entries  : {cardResult.Data.Entries.Count}");
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
        if (!await EnsureSupportedRaceCardScenarioDateAsync(jraAgent, runDate, scenario, CancellationToken.None))
        {
            return;
        }

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
    else if (string.Equals(scenario, "agent-client-api-roundtrip", StringComparison.OrdinalIgnoreCase))
    {
        var apiBaseUrl = apiBaseUrlArg ?? DefaultApiBaseUrl;
        var apiKey = apiKeyArg ?? "dev-api-key";
        var runDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var racecourse = targetUrlArg ?? "東京";
        var raceNumber = raceNumberArg is null
            ? 11
            : int.Parse(raceNumberArg, CultureInfo.InvariantCulture);

        using var serviceProvider = BuildAgentClientApiServiceProvider(apiBaseUrl, apiKey);
        var writeService = serviceProvider.GetRequiredService<IDataCollectionWriteService>();
        var queryService = serviceProvider.GetRequiredService<IRaceQueryService>();

        Console.WriteLine($"ApiBaseUrl : {apiBaseUrl}");
        Console.WriteLine($"Date       : {runDate:yyyy-MM-dd}");
        Console.WriteLine($"Racecourse : {racecourse}");
        Console.WriteLine($"RaceNumber : {raceNumber}R");
        Console.WriteLine();

        var raceId = await writeService.UpsertRaceAsync(
            raceDate: runDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            racecourseCode: racecourse,
            raceNumber: raceNumber,
            raceName: $"Verifier {runDate:yyyyMMdd} {racecourse} {raceNumber}R",
            entryCount: 1,
            gradeCode: null,
            surfaceCode: "T",
            distanceMeters: 1600,
            directionCode: null);

        await writeService.UpsertRaceEntryAsync(
            raceId: raceId,
            horseNumber: 1,
            horseName: "Verifier Horse",
            jockeyName: "Verifier Jockey",
            trainerName: "Verifier Trainer",
            gateNumber: 1,
            assignedWeight: 55m,
            sexCode: "牡",
            age: 3,
            declaredWeight: 480m,
            declaredWeightDiff: 0m);

        var raceContext = await queryService.GetRacePredictionContextAsync(raceId);
        if (raceContext is null)
        {
            Console.WriteLine("NG: API roundtrip 後に RacePredictionContext が取得できませんでした。");
            return;
        }

        Console.WriteLine("OK: AgentClient HTTP roundtrip 成功");
        Console.WriteLine($"RaceId     : {raceContext.RaceId}");
        Console.WriteLine($"RaceName   : {raceContext.RaceName}");
        Console.WriteLine($"Entries    : {raceContext.Entries.Count}");
        foreach (var entry in raceContext.Entries.Take(3))
        {
            Console.WriteLine($"  - {entry.HorseNumber}番 HorseId={entry.HorseId} / JockeyId={entry.JockeyId ?? "-"}");
        }
    }
    else if (string.Equals(scenario, "agent-client-jra-schedule-workflow", StringComparison.OrdinalIgnoreCase))
    {
        var referenceDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lookaheadDays = lookaheadDaysArg is null
            ? 14
            : int.Parse(lookaheadDaysArg, CultureInfo.InvariantCulture);

        var workflow = new JraRaceScheduleCollectionWorkflow();
        var result = await workflow.CollectAsync(referenceDate, lookaheadDays);

        Console.WriteLine($"ReferenceDate : {referenceDate:yyyy-MM-dd}");
        Console.WriteLine($"LookaheadDays : {lookaheadDays}");
        Console.WriteLine($"Collected     : {result.RaceDates.Count}");
        Console.WriteLine($"Upcoming      : {result.UpcomingRaceDates.Count}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            Console.WriteLine($"Error         : {result.Error}");
            return;
        }

        foreach (var date in result.UpcomingRaceDates.Take(20))
        {
            Console.WriteLine($"  - {date:yyyy-MM-dd} ({GetJapaneseDayOfWeek(date.DayOfWeek)})");
        }
    }
    else if (string.Equals(scenario, "agent-client-jra-result-workflow", StringComparison.OrdinalIgnoreCase))
    {
        var apiBaseUrl = apiBaseUrlArg ?? DefaultApiBaseUrl;
        var apiKey = apiKeyArg ?? "dev-api-key";
        var runDate = runDateArg is null
            ? DateOnly.FromDateTime(DateTime.Today.AddDays(-1))
            : DateOnly.ParseExact(runDateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var serviceProvider = BuildAgentClientApiServiceProvider(apiBaseUrl, apiKey);
        var writeTools = serviceProvider.GetRequiredService<DataCollectionWriteTools>();
        var queryService = serviceProvider.GetRequiredService<IRaceQueryService>();

        await using var resultBrowser = await PlaywrightWebBrowser.CreateAsync(searchBaseUrl: options.Value.SearchBaseUrl);
        var scraper = new JraRaceResultScraper(resultBrowser);
        var workflow = new JraRaceResultCollectionWorkflow(
            resultBrowser,
            scraper,
            writeTools,
            queryService,
            loggerFactory.CreateLogger<JraRaceResultCollectionWorkflow>(),
            loggerFactory);

        Console.WriteLine($"ApiBaseUrl : {apiBaseUrl}");
        Console.WriteLine($"RaceDate   : {runDate:yyyy-MM-dd}");
        Console.WriteLine();

        var result = await workflow.CollectAsync(runDate);
        Console.WriteLine($"Discovered : {result.DiscoveredUrls.Count}");
        Console.WriteLine($"Scraped    : {result.ScrapedResults.Count}");
        Console.WriteLine($"Saved      : {result.SavedRaceIds.Count}");
        Console.WriteLine($"Errors     : {result.Errors.Count}");

        foreach (var raceId in result.SavedRaceIds.Take(5))
        {
            var raceContext = await queryService.GetRacePredictionContextAsync(raceId);
            Console.WriteLine($"  - Saved RaceId={raceId} Context={(raceContext is null ? "missing" : "ok")}");
        }

        foreach (var error in result.Errors.Take(10))
        {
            Console.WriteLine($"  - Error: {error}");
        }
    }
    else if (string.Equals(scenario, "agent-client-jra-navigation-agent", StringComparison.OrdinalIgnoreCase))
    {
        var targetUrl = targetUrlArg ?? "https://www.jra.go.jp/keiba/thisweek/";
        await EnsureLmStudioAvailableAsync(baseUri, model, scenario, CancellationToken.None);
        var chatClient = CreateLmStudioChatClient(baseUri, model);
        await using var jraTools = new JraPageExtractionTools();
        var navigationAgent = new JraNavigationAgent(chatClient, jraTools.GetAITools());
        var navigationPrompt = relationArg is null
            ? $"{targetUrl} を開き、現在ページの pageKind、主要な structured 情報、次に利用できる relation を簡潔に要約してください。最後にセッションを閉じてください。"
            : $"{targetUrl} を開き、relation '{relationArg}' で次ページへ進み、到達ページの pageKind と主要情報を要約してください。最後にセッションを閉じてください。";

        Console.WriteLine($"TargetUrl : {targetUrl}");
        if (!string.IsNullOrWhiteSpace(relationArg))
        {
            Console.WriteLine($"Relation  : {relationArg}");
        }
        Console.WriteLine();

        var result = await navigationAgent.InvokeAsync(navigationPrompt);
        Console.WriteLine(result);
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
        await EnsureLmStudioAvailableAsync(baseUri, model, scenario, CancellationToken.None);
        var chatClient = CreateLmStudioChatClient(baseUri, model);
        var pageDataExtractionAgent = new PageDataExtractionAgent(
            chatClient,
            loggerFactory.CreateLogger<PageDataExtractionAgent>(),
            modelId: model,
            profileOverride: extractionProfileArg);
        await using var browser = await PlaywrightWebBrowser.CreateAsync(searchBaseUrl: options.Value.SearchBaseUrl);
        var playwrightTools = new PlaywrightTools(
            browser,
            options,
            pageDataExtractionAgent,
            loggerFactory.CreateLogger<PlaywrightTools>());
        var agent = new WebBrowserAgent(chatClient, playwrightTools.GetAITools());

        Console.WriteLine($"Prompt  : {prompt}");
        var result = await agent.InvokeAsync(prompt);
        Console.WriteLine(result);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Agent invocation failed: {ex.Message}");
}

static IChatClient CreateLmStudioChatClient(string baseUri, string model)
    => new LMStudioChatClient(new LMStudioChatClientOptions
    {
        BaseUri = new Uri(baseUri),
        DefaultModel = model,
    });

static async Task EnsureLmStudioAvailableAsync(
    string baseUri,
    string model,
    string scenario,
    CancellationToken cancellationToken)
{
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    try
    {
        using var response = await httpClient.GetAsync(new Uri(new Uri(baseUri), "/v1/models"), cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException($"LMStudio returned {(int)response.StatusCode} {response.ReasonPhrase}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        throw new InvalidOperationException(
            $"シナリオ '{scenario}' は LMStudio が必要ですが、{baseUri} に接続できませんでした。LMStudio を起動するか、LMSTUDIO_BASEURI / LMSTUDIO_MODEL を確認してください。Model={model}",
            ex);
    }
}

static async Task<bool> EnsureSupportedRaceCardScenarioDateAsync(
    JraTaskAgent jraAgent,
    DateOnly runDate,
    string scenario,
    CancellationToken cancellationToken)
{
    var scheduleReferenceDate = DateOnly.FromDateTime(DateTime.Today);
    var scheduleResult = await jraAgent.RequestRaceScheduleDatesAsync(scheduleReferenceDate, cancellationToken);

    if (!scheduleResult.Success || scheduleResult.Data is null)
    {
        Console.WriteLine("[preflight] 開催日一覧の取得に失敗したため、current-week 前提の検証可否を判定できませんでした。");
        Console.WriteLine($"  Error: {scheduleResult.Error ?? "開催日一覧を取得できませんでした。"}");
        return false;
    }

    if (scheduleResult.Data.RaceDates.Contains(runDate))
    {
        return true;
    }

    Console.WriteLine($"[preflight] シナリオ '{scenario}' は current-week の出馬表導線専用です。");
    Console.WriteLine($"  Requested : {runDate:yyyy-MM-dd}");
    Console.WriteLine("  Supported : " + string.Join(", ", scheduleResult.Data.RaceDates.Select(x => x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
    Console.WriteLine("  Hint      : まず jra-task-agent-schedule-dates で有効日を確認し、その日付を指定してください。");
    return false;
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

static string? FindHoldingLabel(PageSnapshot snapshot, string racecourse)
{
    var regex = new Regex(@"\d+回(東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)\d+日", RegexOptions.CultureInvariant);

    return snapshot.Links
        .Select(link => regex.Match(link.Title ?? string.Empty))
        .Where(match => match.Success && match.Value.Contains(racecourse, StringComparison.Ordinal))
        .Select(match => match.Value)
        .FirstOrDefault()
        ?? snapshot.Actions
            .Select(action => regex.Match(action.Text ?? string.Empty))
            .Where(match => match.Success && match.Value.Contains(racecourse, StringComparison.Ordinal))
            .Select(match => match.Value)
            .FirstOrDefault()
        ?? regex.Matches(snapshot.MainText ?? string.Empty)
            .Select(match => match.Value)
            .Where(value => value.Contains(racecourse, StringComparison.Ordinal))
            .FirstOrDefault();
}

static string? FindRaceLink(PageSnapshot snapshot, DateOnly raceDate, string racecourse, int raceNumber)
{
    var dayText = $"{raceDate.Month}月{raceDate.Day}日";
    var compactDate = raceDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    return snapshot.Links
        .Select(link => new
        {
            link.Title,
            Url = ResolveUrl(snapshot.Url, link.Url),
            MatchesCourse = !string.IsNullOrWhiteSpace(link.Title)
                && link.Title.Contains(racecourse, StringComparison.Ordinal),
            MatchesRaceNumber = !string.IsNullOrWhiteSpace(link.Title)
                && (link.Title.Contains($"{raceNumber}R", StringComparison.OrdinalIgnoreCase)
                    || link.Title.Contains($"第{raceNumber}レース", StringComparison.Ordinal)
                    || link.Title.Contains($"{raceNumber}レース", StringComparison.Ordinal)),
            MatchesDate = (!string.IsNullOrWhiteSpace(link.Title)
                    && link.Title.Contains(dayText, StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(link.Url)
                    && link.Url.Contains(compactDate, StringComparison.Ordinal)),
        })
        .Where(link => !string.IsNullOrWhiteSpace(link.Url)
            && link.MatchesCourse
            && link.MatchesRaceNumber
            && !link.Url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(link => link.MatchesDate)
        .Select(link => link.Url)
        .FirstOrDefault();
}

static async Task<string?> TryOpenRaceNumberAsync(JraTaskAgent jraAgent, int raceNumber)
{
    foreach (var candidate in BuildRaceNumberClickCandidates(raceNumber))
    {
        try
        {
            await jraAgent.FollowAsync(candidate);
            return candidate;
        }
        catch
        {
        }
    }

    return null;
}

static IReadOnlyList<string> BuildRaceNumberClickCandidates(int raceNumber)
{
    var baseNumber = raceNumber.ToString(CultureInfo.InvariantCulture);

    return new[]
    {
        $"{baseNumber}レース",
        $"第{baseNumber}レース",
        $"{baseNumber}R",
        $"{baseNumber}Ｒ",
        baseNumber,
    };
}

static string? ResolveUrl(string? baseUrl, string? candidateUrl)
{
    if (string.IsNullOrWhiteSpace(candidateUrl))
    {
        return candidateUrl;
    }

    if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var absoluteUri))
    {
        return absoluteUri.ToString();
    }

    if (string.IsNullOrWhiteSpace(baseUrl)
        || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var sourceUri)
        || !Uri.TryCreate(sourceUri, candidateUrl, out var resolvedUri))
    {
        return candidateUrl;
    }

    return resolvedUri.ToString();
}

static ServiceProvider BuildAgentClientApiServiceProvider(string apiBaseUrl, string apiKey)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.Configure<ApiClientOptions>(options =>
    {
        options.BaseUrl = apiBaseUrl;
        options.ApiKey = apiKey;
    });
    services.AddHttpAgentServices();
    return services.BuildServiceProvider();
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

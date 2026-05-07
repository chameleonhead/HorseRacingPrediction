using System.Globalization;
using System.Text;
using System.Text.Json;
using EventFlow.Queries;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.ChatClients;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
var dbPathArg = GetArgValue(args, "--db-path");

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
    else if (string.Equals(scenario, "jra-race-results", StringComparison.OrdinalIgnoreCase))
    {
        var entryUrl = targetUrlArg ?? JraResultTopPageScraper.DefaultEntryUrl;
        var maxScrapes = maxScrapesArg is null
            ? 1
            : int.Parse(maxScrapesArg, CultureInfo.InvariantCulture);

        var topScraper = new JraResultTopPageScraper(browser);
        var raceListScraper = new JraResultRaceListScraper(browser);
        var raceScraper = new JraRaceResultScraper(browser);

        Console.WriteLine($"Entry   : {entryUrl}");
        Console.WriteLine();

        Console.WriteLine("[Layer 1] keiba/ → レース結果クリック → 開催日+開催ボタンを取得中...");
        var dayCourseLinks = await topScraper.ScrapeAsync(entryUrl);

        if (dayCourseLinks is null || dayCourseLinks.Count == 0)
        {
            Console.WriteLine("[Layer 1] 開催ボタンが見つかりませんでした。");
            return;
        }

        Console.WriteLine($"[Layer 1] {dayCourseLinks.Count} 件");
        foreach (var link in dayCourseLinks)
        {
            Console.WriteLine($"  {link.RaceDate:yyyy-MM-dd} {link.Racecourse ?? "-"} [{link.Label}]");
        }

        Console.WriteLine();

        foreach (var dayCourse in dayCourseLinks.Take(maxScrapes))
        {
            Console.WriteLine($"[Layer 2] '{dayCourse.Label}' をクリック → レース番号取得中...");

            IReadOnlyList<int>? raceNumbers;
            try
            {
                raceNumbers = await raceListScraper.ScrapeAsync(dayCourse.Label);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Layer 2] クリック失敗: {ex.Message}");
                continue;
            }

            if (raceNumbers is null || raceNumbers.Count == 0)
            {
                Console.WriteLine("[Layer 2] レース番号が見つかりませんでした。");
                await browser.GoBackAsync();
                continue;
            }

            Console.WriteLine($"[Layer 2] {raceNumbers.Count} 件: {string.Join(", ", raceNumbers.Select(n => $"{n}R"))}");
            Console.WriteLine();

            var firstRaceNumber = raceNumbers.OrderBy(n => n).First();
            var raceButtonText = $"{firstRaceNumber}レース";
            Console.WriteLine($"[Layer 3] '{raceButtonText}' をクリック → 結果取得中...");

            try
            {
                await browser.ClickAsync(raceButtonText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Layer 3] クリック失敗: {ex.Message}");
                await browser.GoBackAsync();
                continue;
            }

            var result = await raceScraper.ScrapeCurrentPageAsync();
            if (result is null)
            {
                Console.WriteLine("[Layer 3] 取得失敗。");
                await browser.GoBackAsync();
                await browser.GoBackAsync();
                continue;
            }

            Console.WriteLine($"[Layer 3] {result.RaceDate:yyyy-MM-dd} {result.Racecourse} {result.RaceNumber}R {result.RaceName}");
            Console.WriteLine($"  コース: {result.CourseType}{result.Distance}m / グレード: {result.Grade ?? "-"}");
            Console.WriteLine($"  着順結果 ({result.Entries.Count} 頭):");
            foreach (var entry in result.Entries.OrderBy(e => e.FinishPosition).Take(5))
            {
                Console.WriteLine($"    {entry.FinishPosition}着 {entry.HorseNumber}番 {entry.HorseName} ({entry.JockeyName}) {entry.OfficialTime}");
            }

            Console.WriteLine();

            // 結果ページ → レース一覧ページ → 開催選択ページへ戻す
            await browser.GoBackAsync();
            await browser.GoBackAsync();
        }
    }
    else if (string.Equals(scenario, "jra-race-results-eventflow-save", StringComparison.OrdinalIgnoreCase))
    {
        var entryUrl = targetUrlArg ?? JraResultTopPageScraper.DefaultEntryUrl;
        var maxScrapes = maxScrapesArg is null
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

        var topScraper = new JraResultTopPageScraper(browser);
        var raceListScraper = new JraResultRaceListScraper(browser);
        var raceScraper = new JraRaceResultScraper(browser);

        Console.WriteLine($"Entry   : {entryUrl}");
        Console.WriteLine($"DB      : {dbPath}");
        Console.WriteLine();

        Console.WriteLine("[Layer 1] keiba/ → レース結果クリック → 開催日+開催ボタンを取得中...");
        var dayCourseLinks = await topScraper.ScrapeAsync(entryUrl);

        if (dayCourseLinks is null || dayCourseLinks.Count == 0)
        {
            Console.WriteLine("[Layer 1] 開催ボタンが見つかりませんでした。");
            return;
        }

        Console.WriteLine($"[Layer 1] {dayCourseLinks.Count} 件");
        foreach (var link in dayCourseLinks)
        {
            Console.WriteLine($"  {link.RaceDate:yyyy-MM-dd} {link.Racecourse ?? "-"} [{link.Label}]");
        }

        Console.WriteLine();

        foreach (var dayCourse in dayCourseLinks.Take(maxScrapes))
        {
            Console.WriteLine($"[Layer 2] '{dayCourse.Label}' をクリック → レース番号取得中...");

            IReadOnlyList<int>? raceNumbers;
            try
            {
                raceNumbers = await raceListScraper.ScrapeAsync(dayCourse.Label);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Layer 2] クリック失敗: {ex.Message}");
                continue;
            }

            if (raceNumbers is null || raceNumbers.Count == 0)
            {
                Console.WriteLine("[Layer 2] レース番号が見つかりませんでした。");
                await browser.GoBackAsync();
                continue;
            }

            Console.WriteLine($"[Layer 2] {raceNumbers.Count} 件: {string.Join(", ", raceNumbers.Select(n => $"{n}R"))}");

            var firstRaceNumber = raceNumbers.OrderBy(n => n).First();
            var raceButtonText = $"{firstRaceNumber}レース";
            Console.WriteLine($"[Layer 3] '{raceButtonText}' をクリック → 結果取得中...");

            try
            {
                await browser.ClickAsync(raceButtonText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Layer 3] クリック失敗: {ex.Message}");
                await browser.GoBackAsync();
                continue;
            }

            JraRaceResultData? result;
            try
            {
                result = await raceScraper.ScrapeCurrentPageAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Layer 3] スクレイプ失敗: {ex.Message}");
                await browser.GoBackAsync();
                await browser.GoBackAsync();
                continue;
            }

            if (result is null)
            {
                Console.WriteLine("[Layer 3] 取得失敗。");
                await browser.GoBackAsync();
                await browser.GoBackAsync();
                continue;
            }

            var sourceUrl = string.IsNullOrWhiteSpace(browser.CurrentUrl) ? result.Url : browser.CurrentUrl;
            var source = JraRaceResultUrl.ParseFromUrl(sourceUrl, result.Racecourse ?? dayCourse.Racecourse);

            var raceId = await SaveScrapedResultToEventFlowAsync(writeTools, source, result);
            if (string.IsNullOrWhiteSpace(raceId))
            {
                Console.WriteLine("[Save] 保存スキップ: レース同定に必要な情報が不足しています。");
                await browser.GoBackAsync();
                await browser.GoBackAsync();
                continue;
            }

            var readModel = await queryProcessor.ProcessAsync(
                new ReadModelByIdQuery<RaceResultViewReadModel>(raceId),
                CancellationToken.None);
            if (readModel is null || string.IsNullOrWhiteSpace(readModel.RaceId))
            {
                Console.WriteLine($"[Verify] NG: RaceId={raceId} のReadModelが取得できません。");
            }
            else
            {
                var winner = result.Entries.FirstOrDefault(e => e.FinishPosition == 1)?.HorseName;
                var winnerMatched = !string.IsNullOrWhiteSpace(winner)
                    && string.Equals(winner, readModel.WinningHorseName, StringComparison.Ordinal);

                Console.WriteLine($"[Verify] RaceId={raceId}");
                Console.WriteLine($"  ステータス: {readModel.Status}");
                Console.WriteLine($"  着順件数: scraped={result.Entries.Count} db={readModel.EntryResults.Count}");
                Console.WriteLine($"  勝ち馬一致: {(winnerMatched ? "OK" : "NG")} (scraped={winner ?? "-"} / db={readModel.WinningHorseName ?? "-"})");
                Console.WriteLine($"  払戻登録: {(readModel.PayoutResult is null ? "なし" : "あり")}");
            }

            Console.WriteLine();

            // 結果ページ → レース一覧ページ → 開催選択ページへ戻す
            await browser.GoBackAsync();
            await browser.GoBackAsync();
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

static async Task<string?> SaveScrapedResultToEventFlowAsync(
    DataCollectionWriteTools writeTools,
    JraRaceResultUrl source,
    JraRaceResultData data)
{
    var raceDate = data.RaceDate ?? source.RaceDate;
    var raceNumber = data.RaceNumber ?? source.RaceNumber;
    var racecourseCode = ResolveRacecourseCode(data.Racecourse, source.RacecourseCode, source.Racecourse);

    if (raceDate is null || raceNumber is null || string.IsNullOrWhiteSpace(racecourseCode))
    {
        return null;
    }

    var raceName = string.IsNullOrWhiteSpace(data.RaceName)
        ? $"R{raceNumber.Value}"
        : data.RaceName;

    var raceId = await writeTools.UpsertRace(
        raceDate: raceDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        racecourseCode: racecourseCode,
        raceNumber: raceNumber.Value,
        raceName: raceName,
        entryCount: data.Entries.Count > 0 ? data.Entries.Count : null,
        gradeCode: data.Grade,
        surfaceCode: data.CourseType,
        distanceMeters: data.Distance);

    foreach (var entry in data.Entries)
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

    var winner = data.Entries.FirstOrDefault(e => e.FinishPosition == 1);
    if (winner is null)
    {
        return raceId;
    }

    await writeTools.DeclareRaceResult(
        raceId: raceId,
        winningHorseName: winner.HorseName);

    foreach (var entry in data.Entries)
    {
        await writeTools.DeclareRaceEntryResult(
            raceId: raceId,
            horseNumber: entry.HorseNumber,
            finishPosition: entry.FinishPosition,
            officialTime: entry.OfficialTime,
            marginText: entry.MarginText,
            lastThreeFurlongTime: entry.LastThreeFurlongTime,
            abnormalResultCode: entry.AbnormalResultCode,
            prizeMoney: null);
    }

    if (data.Payouts is not null)
    {
        await writeTools.DeclareRacePayouts(
            raceId: raceId,
            winPayoutsJson: ToPayoutJson(data.Payouts.WinPayouts),
            placePayoutsJson: ToPayoutJson(data.Payouts.PlacePayouts),
            quinellaPayoutsJson: ToPayoutJson(data.Payouts.QuinellaPayouts),
            exactaPayoutsJson: ToPayoutJson(data.Payouts.ExactaPayouts),
            trifectaPayoutsJson: ToPayoutJson(data.Payouts.TrifectaPayouts));
    }

    return raceId;
}

static string? ToPayoutJson(IReadOnlyList<JraPayoutEntry> entries)
{
    if (entries.Count == 0)
    {
        return null;
    }

    var payload = entries.Select(e => new { combination = e.Combination, amount = e.Amount });
    return JsonSerializer.Serialize(payload);
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

static string? ResolveRacecourseCode(string? racecourse, string? racecourseCode, string? fallbackRacecourse)
{
    if (!string.IsNullOrWhiteSpace(racecourseCode))
    {
        return racecourseCode;
    }

    var key = !string.IsNullOrWhiteSpace(racecourse)
        ? racecourse.Trim()
        : fallbackRacecourse?.Trim();

    if (string.IsNullOrWhiteSpace(key))
    {
        return null;
    }

    return key switch
    {
        "札幌" => "01",
        "函館" => "02",
        "福島" => "03",
        "新潟" => "04",
        "東京" => "05",
        "中山" => "06",
        "中京" => "07",
        "京都" => "08",
        "阪神" => "09",
        "小倉" => "10",
        _ => key,
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

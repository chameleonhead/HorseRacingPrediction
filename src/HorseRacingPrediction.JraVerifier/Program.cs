using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Scraping.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.JraVerifier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var targets = args.Length == 0 ? ["all"] : args;
var useApiWrite = targets.Any(arg =>
    string.Equals(arg, "write-api", StringComparison.OrdinalIgnoreCase)
    || string.Equals(arg, "--write=api", StringComparison.OrdinalIgnoreCase));

var optionMap = targets
    .Where(arg => arg.StartsWith("--", StringComparison.Ordinal))
    .Select(ParseOption)
    .Where(x => !string.IsNullOrWhiteSpace(x.Key))
    .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);

var timeoutSeconds = TryParseIntOption(optionMap, "timeout-seconds") ?? 180;
timeoutSeconds = Math.Clamp(timeoutSeconds, 10, 3600);
var operationTimeout = TimeSpan.FromSeconds(timeoutSeconds);
var maxRaces = TryParseIntOption(optionMap, "max-races");

var raceCardDate = TryParseDateOption(optionMap, "race-card-date")
    ?? TryParseDateOption(optionMap, "date")
    ?? new DateOnly(2026, 5, 23);
var raceCardUrl = optionMap.TryGetValue("race-card-url", out var directRaceCardUrl)
    && !string.IsNullOrWhiteSpace(directRaceCardUrl)
    ? directRaceCardUrl.Trim()
    : null;

var resultDates = BuildResultDates(optionMap);

var scenarioTargets = targets
    .Where(arg => !string.Equals(arg, "write-api", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(arg, "--write=api", StringComparison.OrdinalIgnoreCase)
        && !arg.StartsWith("--", StringComparison.Ordinal))
    .ToArray();

var writeService = useApiWrite ? CreateApiWriteService() : new NoOpDataCollectionWriteService();
var writeTools = new DataCollectionWriteTools(writeService);
var sessionFactory = new PlaywrightWebBrowserSessionFactory();

Console.WriteLine($"[Verifier] WriteMode={(useApiWrite ? "api" : "noop")}");
Console.WriteLine($"[Verifier] TimeoutSeconds={timeoutSeconds}");

if (scenarioTargets.Contains("all", StringComparer.OrdinalIgnoreCase) || scenarioTargets.Contains("race-card", StringComparer.OrdinalIgnoreCase))
{
    await VerifyRaceCardCollectionAsync(raceCardDate, operationTimeout);
}

if (scenarioTargets.Contains("all", StringComparer.OrdinalIgnoreCase) || scenarioTargets.Contains("result-month", StringComparer.OrdinalIgnoreCase))
{
    await VerifyResultMonthDatesAsync(2026, 5);
}

if (scenarioTargets.Contains("all", StringComparer.OrdinalIgnoreCase) || scenarioTargets.Contains("result-day", StringComparer.OrdinalIgnoreCase))
{
    foreach (var date in resultDates)
    {
        await VerifyResultDayAsync(date, operationTimeout);
    }
}

return;

async Task VerifyRaceCardCollectionAsync(DateOnly raceDate, TimeSpan timeout)
{
    Console.WriteLine($"[RaceCard] Date={raceDate:yyyy-MM-dd}");
    await using var browser = await sessionFactory.CreateAsync();
    var workflow = new JraRaceCardCollectionWorkflow(browser, new JraRaceCardScraper(browser), writeTools);
    using var cts = new CancellationTokenSource(timeout);
    IReadOnlyList<JraRaceCardUrl> discovered;

    if (!string.IsNullOrWhiteSpace(raceCardUrl))
    {
        discovered = [JraRaceCardUrl.ParseFromUrl(raceCardUrl)];
        Console.WriteLine("  mode=direct-url");
    }
    else
    {
        try
        {
            discovered = await workflow.DiscoverUrlsAsync(raceDate, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"  timeout=discovery>{timeout.TotalSeconds:0}s");
            return;
        }
    }

    if (maxRaces is > 0 && discovered.Count > maxRaces.Value)
    {
        discovered = discovered.Take(maxRaces.Value).ToArray();
        Console.WriteLine($"  maxRaces={maxRaces.Value}");
    }

    Console.WriteLine($"  discovered={discovered.Count}");
    foreach (var item in discovered)
    {
        Console.WriteLine($"  - discovered url={item.Url} course={item.Racecourse ?? item.RacecourseCode} race={item.RaceNumber}");
    }

    IReadOnlyList<(JraRaceCardUrl Source, JraRaceCardData Data)> scraped;
    try
    {
        scraped = await workflow.ScrapeAllAsync(discovered, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"  timeout=scrape>{timeout.TotalSeconds:0}s");
        return;
    }

    Console.WriteLine($"  scraped={scraped.Count}");
    foreach (var item in scraped)
    {
        Console.WriteLine($"  - scraped race={item.Data.RaceDate:yyyy-MM-dd} {item.Data.Racecourse} {item.Data.RaceNumber}R {item.Data.RaceName} entries={item.Data.Entries.Count}");
    }

    (IReadOnlyList<string> SavedRaceIds, IReadOnlyList<string> Errors) saved;
    try
    {
        saved = await workflow.SaveAllAsync(scraped, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"  timeout=save>{timeout.TotalSeconds:0}s");
        return;
    }

    PrintSaveSummary(saved.SavedRaceIds, saved.Errors);

    if (discovered.Count == 0)
    {
        await DumpRaceCardDiscoveryContextAsync(browser, cts.Token);
    }
}

async Task DumpRaceCardDiscoveryContextAsync(IWebBrowser browser, CancellationToken cancellationToken)
{
    Console.WriteLine("  debug=thisweek");
    await browser.NavigateAsync("https://www.jra.go.jp/keiba/", cancellationToken);
    await browser.ClickAsync("今週の注目レース", cancellationToken);
    var thisWeek = await browser.GetPageSnapshotAsync(50, cancellationToken);
    Console.WriteLine($"  - title={thisWeek.Title}");
    foreach (var heading in thisWeek.Headings.Take(10))
    {
        Console.WriteLine($"  - heading={heading}");
    }

    foreach (var action in thisWeek.Actions.Take(20))
    {
        Console.WriteLine($"  - action={action.Text}");
    }

    foreach (var link in thisWeek.Links
        .Where(link => (link.Url ?? string.Empty).Contains("accessD", StringComparison.OrdinalIgnoreCase)
            || (link.Url ?? string.Empty).Contains("syutsuba", StringComparison.OrdinalIgnoreCase)
            || (link.Title ?? string.Empty).Contains("出馬表", StringComparison.Ordinal))
        .Take(20))
    {
        Console.WriteLine($"  - link title={link.Title} url={link.Url}");
    }

    Console.WriteLine("  debug=menu-racecard");
    await browser.NavigateAsync("https://www.jra.go.jp/keiba/", cancellationToken);
    await browser.ClickAsync("出馬表", cancellationToken);
    var holdings = await browser.GetPageSnapshotAsync(50, cancellationToken);
    Console.WriteLine($"  - title={holdings.Title}");
    foreach (var heading in holdings.Headings.Take(10))
    {
        Console.WriteLine($"  - heading={heading}");
    }

    foreach (var action in holdings.Actions.Take(20))
    {
        Console.WriteLine($"  - action={action.Text}");
    }

    foreach (var link in holdings.Links
        .Where(link => (link.Url ?? string.Empty).Contains("accessD", StringComparison.OrdinalIgnoreCase)
            || (link.Url ?? string.Empty).Contains("syutsuba", StringComparison.OrdinalIgnoreCase)
            || (link.Title ?? string.Empty).Contains("出馬表", StringComparison.Ordinal))
        .Take(20))
    {
        Console.WriteLine($"  - link title={link.Title} url={link.Url}");
    }

    try
    {
        await browser.ClickAsync("5月23日（土曜）", cancellationToken);
        var selectedDay = await browser.GetPageSnapshotAsync(50, cancellationToken);
        Console.WriteLine("  debug=menu-racecard-clicked-day");
        Console.WriteLine($"  - title={selectedDay.Title}");
        foreach (var heading in selectedDay.Headings.Take(10))
        {
            Console.WriteLine($"  - heading={heading}");
        }

        foreach (var action in selectedDay.Actions.Take(20))
        {
            Console.WriteLine($"  - action={action.Text}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  - clicked-day-error={ex.Message}");
    }

    try
    {
        await browser.NavigateAsync("https://www.jra.go.jp/keiba/rpdf/", cancellationToken);
        var venuePage = await browser.GetPageSnapshotAsync(50, cancellationToken);
        Console.WriteLine("  debug=venue-racecard");
        Console.WriteLine($"  - title={venuePage.Title}");
        foreach (var heading in venuePage.Headings.Take(10))
        {
            Console.WriteLine($"  - heading={heading}");
        }

        foreach (var link in venuePage.Links
            .Where(link => (link.Url ?? string.Empty).Contains("accessD", StringComparison.OrdinalIgnoreCase)
                || (link.Url ?? string.Empty).Contains("syutsuba", StringComparison.OrdinalIgnoreCase)
                || (link.Title ?? string.Empty).Contains("出馬表", StringComparison.Ordinal))
            .Take(20))
        {
            Console.WriteLine($"  - link title={link.Title} url={link.Url}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  - venue-racecard-error={ex.Message}");
    }
}

async Task VerifyResultMonthDatesAsync(int year, int month)
{
    Console.WriteLine($"[ResultMonth] Month={year:D4}-{month:D2}");
    var service = new JraResultMonthDateDiscoveryService(sessionFactory, new JraResultDateParser());
    var dates = await service.DiscoverMonthDatesAsync(year, month);
    Console.WriteLine($"  dates={dates.Count}");
    Console.WriteLine($"  contains 2026-05-02={dates.Contains(new DateOnly(2026, 5, 2))}");
    Console.WriteLine($"  contains 2026-05-03={dates.Contains(new DateOnly(2026, 5, 3))}");
    foreach (var date in dates.Take(20))
    {
        Console.WriteLine($"  - date={date:yyyy-MM-dd}");
    }
}

async Task VerifyResultDayAsync(DateOnly raceDate, TimeSpan timeout)
{
    Console.WriteLine($"[ResultDay] Date={raceDate:yyyy-MM-dd}");
    await using var browser = await sessionFactory.CreateAsync();
    var workflow = new JraRaceResultCollectionWorkflow(browser, new JraRaceResultScraper(browser), writeTools);
    using var cts = new CancellationTokenSource(timeout);
    IReadOnlyList<JraRaceResultUrl> discovered;
    try
    {
        discovered = await workflow.DiscoverUrlsAsync(raceDate, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"  timeout=discovery>{timeout.TotalSeconds:0}s");
        return;
    }

    Console.WriteLine($"  discovered={discovered.Count}");
    foreach (var item in discovered)
    {
        Console.WriteLine($"  - url={item.Url} course={item.Racecourse ?? item.RacecourseCode} race={item.RaceNumber}");
    }

    IReadOnlyList<(JraRaceResultUrl Source, JraRaceResultData Data)> scraped;
    try
    {
        scraped = await workflow.ScrapeAllAsync(discovered, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"  timeout=scrape>{timeout.TotalSeconds:0}s");
        return;
    }

    Console.WriteLine($"  scraped={scraped.Count}");

    (IReadOnlyList<string> SavedRaceIds, IReadOnlyList<string> Errors) saved;
    try
    {
        saved = await workflow.SaveAllAsync(scraped, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"  timeout=save>{timeout.TotalSeconds:0}s");
        return;
    }

    PrintSaveSummary(saved.SavedRaceIds, saved.Errors);

    foreach (var item in discovered.Take(3))
    {
        var racecourse = ResolveRacecourse(item.Racecourse, item.RacecourseCode);
        if (racecourse is null || item.RaceNumber is null || item.RaceDate is null)
        {
            continue;
        }

        await using var taskAgent = await JraTaskAgent.CreateAsync();
        var result = await taskAgent.RequestRaceResultAsync(item.RaceDate.Value, racecourse, item.RaceNumber.Value, cts.Token);
        Console.WriteLine($"  - click result success={result.Success} course={racecourse} race={item.RaceNumber} source={result.SourceUrl}");
    }
}

static (string Key, string Value) ParseOption(string raw)
{
    var trimmed = raw.Trim();
    if (!trimmed.StartsWith("--", StringComparison.Ordinal))
    {
        return (string.Empty, string.Empty);
    }

    var payload = trimmed[2..];
    var separatorIndex = payload.IndexOf('=');
    if (separatorIndex < 0)
    {
        return (payload, "true");
    }

    var key = payload[..separatorIndex];
    var value = payload[(separatorIndex + 1)..];
    return (key, value);
}

static int? TryParseIntOption(IReadOnlyDictionary<string, string> options, string key)
{
    if (!options.TryGetValue(key, out var raw))
    {
        return null;
    }

    return int.TryParse(raw, out var parsed) ? parsed : null;
}

static DateOnly? TryParseDateOption(IReadOnlyDictionary<string, string> options, string key)
{
    if (!options.TryGetValue(key, out var raw))
    {
        return null;
    }

    return DateOnly.TryParse(raw, out var parsed) ? parsed : null;
}

static IReadOnlyList<DateOnly> BuildResultDates(IReadOnlyDictionary<string, string> options)
{
    var explicitDate = TryParseDateOption(options, "result-day-date") ?? TryParseDateOption(options, "date");
    if (explicitDate is not null)
    {
        return [explicitDate.Value];
    }

    return
    [
        new DateOnly(2026, 5, 2),
        new DateOnly(2026, 5, 3)
    ];
}

static string? ResolveRacecourse(string? racecourse, string? racecourseCode)
{
    if (!string.IsNullOrWhiteSpace(racecourse))
    {
        return racecourse;
    }

    return racecourseCode switch
    {
        "01" => "札幌",
        "02" => "函館",
        "03" => "福島",
        "04" => "新潟",
        "05" => "東京",
        "06" => "中山",
        "07" => "中京",
        "08" => "京都",
        "09" => "阪神",
        "10" => "小倉",
        _ => null,
    };
}

IDataCollectionWriteService CreateApiWriteService()
{
    var baseUrl = Environment.GetEnvironmentVariable("HRP_API_BASE_URL") ?? "http://localhost:5177";
    var apiKey = Environment.GetEnvironmentVariable("HRP_API_KEY") ?? "dev-api-key";

    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(baseUrl)
    };
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    var stateDir = Path.Combine(Path.GetTempPath(), "hrp-jra-verifier-state");
    var options = Options.Create(new AgentProcessingOptions
    {
        StateDirectory = stateDir,
        JobStoreFileName = "verifier-processing-jobs.db"
    });
    var stateStore = new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance);
    var recorder = new AgentAcquisitionStatusRecorder(stateStore);
    return new HttpDataCollectionWriteService(httpClient, recorder);
}

void PrintSaveSummary(IReadOnlyList<string> savedRaceIds, IReadOnlyList<string> errors)
{
    var conflictSkips = new List<string>();
    var hardErrors = new List<string>();

    foreach (var error in errors)
    {
        if (IsConflictOrAlreadyProcessed(error))
        {
            conflictSkips.Add(error);
        }
        else
        {
            hardErrors.Add(error);
        }
    }

    Console.WriteLine($"  savedRaceIds={savedRaceIds.Count}");
    Console.WriteLine($"  saveSkipped={conflictSkips.Count} (already processed)");
    Console.WriteLine($"  saveErrors={hardErrors.Count}");

    foreach (var skipped in conflictSkips.Take(3))
    {
        Console.WriteLine($"  - saveSkip={skipped}");
    }

    foreach (var hardError in hardErrors.Take(5))
    {
        Console.WriteLine($"  - saveError={hardError}");
    }
}

static bool IsConflictOrAlreadyProcessed(string message)
{
    return message.Contains(" 409 ", StringComparison.Ordinal)
        || message.Contains("409 (Conflict)", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Response status code does not indicate success: 409", StringComparison.OrdinalIgnoreCase)
        || message.Contains("既に登録済み", StringComparison.Ordinal)
        || message.Contains("既に記録済み", StringComparison.Ordinal)
        || message.Contains("already", StringComparison.OrdinalIgnoreCase);
}
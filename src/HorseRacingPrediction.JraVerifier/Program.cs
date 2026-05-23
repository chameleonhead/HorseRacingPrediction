using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.JraVerifier;

var targets = args.Length == 0 ? ["all"] : args;
var writeTools = new DataCollectionWriteTools(new NoOpDataCollectionWriteService());
var sessionFactory = new PlaywrightWebBrowserSessionFactory();

if (targets.Contains("all", StringComparer.OrdinalIgnoreCase) || targets.Contains("race-card", StringComparer.OrdinalIgnoreCase))
{
    await VerifyRaceCardCollectionAsync(new DateOnly(2026, 5, 23));
}

if (targets.Contains("all", StringComparer.OrdinalIgnoreCase) || targets.Contains("result-month", StringComparer.OrdinalIgnoreCase))
{
    await VerifyResultMonthDatesAsync(2026, 5);
}

if (targets.Contains("all", StringComparer.OrdinalIgnoreCase) || targets.Contains("result-day", StringComparer.OrdinalIgnoreCase))
{
    await VerifyResultDayAsync(new DateOnly(2026, 5, 2));
    await VerifyResultDayAsync(new DateOnly(2026, 5, 3));
}

return;

async Task VerifyRaceCardCollectionAsync(DateOnly raceDate)
{
    Console.WriteLine($"[RaceCard] Date={raceDate:yyyy-MM-dd}");
    await using var browser = await sessionFactory.CreateAsync();
    var workflow = new JraRaceCardCollectionWorkflow(browser, new JraRaceCardScraper(browser), writeTools);
    var discovered = await workflow.DiscoverUrlsAsync(raceDate);
    Console.WriteLine($"  discovered={discovered.Count}");
    foreach (var item in discovered)
    {
        Console.WriteLine($"  - discovered url={item.Url} course={item.Racecourse ?? item.RacecourseCode} race={item.RaceNumber}");
    }

    var scraped = await workflow.ScrapeAllAsync(discovered);
    Console.WriteLine($"  scraped={scraped.Count}");
    foreach (var item in scraped)
    {
        Console.WriteLine($"  - scraped race={item.Data.RaceDate:yyyy-MM-dd} {item.Data.Racecourse} {item.Data.RaceNumber}R {item.Data.RaceName} entries={item.Data.Entries.Count}");
    }

    if (discovered.Count == 0)
    {
        await DumpRaceCardDiscoveryContextAsync(browser);
    }
}

async Task DumpRaceCardDiscoveryContextAsync(IWebBrowser browser)
{
    Console.WriteLine("  debug=thisweek");
    await browser.NavigateAsync("https://www.jra.go.jp/keiba/", CancellationToken.None);
    await browser.ClickAsync("今週の注目レース", CancellationToken.None);
    var thisWeek = await browser.GetPageSnapshotAsync(50, CancellationToken.None);
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
    await browser.NavigateAsync("https://www.jra.go.jp/keiba/", CancellationToken.None);
    await browser.ClickAsync("出馬表", CancellationToken.None);
    var holdings = await browser.GetPageSnapshotAsync(50, CancellationToken.None);
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
        await browser.ClickAsync("5月23日（土曜）", CancellationToken.None);
        var selectedDay = await browser.GetPageSnapshotAsync(50, CancellationToken.None);
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
        await browser.NavigateAsync("https://www.jra.go.jp/keiba/rpdf/", CancellationToken.None);
        var venuePage = await browser.GetPageSnapshotAsync(50, CancellationToken.None);
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

async Task VerifyResultDayAsync(DateOnly raceDate)
{
    Console.WriteLine($"[ResultDay] Date={raceDate:yyyy-MM-dd}");
    await using var browser = await sessionFactory.CreateAsync();
    var workflow = new JraRaceResultCollectionWorkflow(browser, new JraRaceResultScraper(browser), writeTools);
    var discovered = await workflow.DiscoverUrlsAsync(raceDate);
    Console.WriteLine($"  discovered={discovered.Count}");
    foreach (var item in discovered)
    {
        Console.WriteLine($"  - url={item.Url} course={item.Racecourse ?? item.RacecourseCode} race={item.RaceNumber}");
    }

    var scraped = await workflow.ScrapeAllAsync(discovered);
    Console.WriteLine($"  scraped={scraped.Count}");

    var saved = await workflow.SaveAllAsync(scraped);
    Console.WriteLine($"  savedRaceIds={saved.SavedRaceIds.Count}");
    Console.WriteLine($"  saveErrors={saved.Errors.Count}");
    foreach (var error in saved.Errors.Take(5))
    {
        Console.WriteLine($"  - saveError={error}");
    }

    foreach (var item in discovered.Take(3))
    {
        var racecourse = ResolveRacecourse(item.Racecourse, item.RacecourseCode);
        if (racecourse is null || item.RaceNumber is null || item.RaceDate is null)
        {
            continue;
        }

        await using var taskAgent = await JraTaskAgent.CreateAsync();
        var result = await taskAgent.RequestRaceResultAsync(item.RaceDate.Value, racecourse, item.RaceNumber.Value);
        Console.WriteLine($"  - click result success={result.Success} course={racecourse} race={item.RaceNumber} source={result.SourceUrl}");
    }
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
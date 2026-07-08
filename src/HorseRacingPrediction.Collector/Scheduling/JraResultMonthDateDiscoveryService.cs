using System.Globalization;
using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JraResultMonthDateDiscoveryService : IJraResultDateDiscoveryService
{
    private sealed record MonthNavigationCandidate(string Text, string? Url);

    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private readonly IWebBrowserSessionFactory _browserSessionFactory;
    private readonly JraResultDateParser _parser;

    public JraResultMonthDateDiscoveryService(
        IWebBrowserSessionFactory browserSessionFactory,
        JraResultDateParser parser)
    {
        _browserSessionFactory = browserSessionFactory;
        _parser = parser;
    }

    public async Task<IReadOnlyList<DateOnly>> DiscoverMonthDatesAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var targetMonth = new DateOnly(year, month, 1);
        await using var browser = await _browserSessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        await browser.NavigateAsync("https://www.jra.go.jp/keiba/", cancellationToken).ConfigureAwait(false);
        await browser.ClickAsync("レース結果", cancellationToken).ConfigureAwait(false);

        var snapshot = await GetMergedSnapshotAsync(browser, cancellationToken).ConfigureAwait(false);
        var currentMonth = GetCurrentMonth();
        var previousMonth = currentMonth.AddMonths(-1);
        var preferTopPage = targetMonth == currentMonth || targetMonth == previousMonth;

        var dates = _parser.ParseMonthDates(snapshot, year, month);
        if (preferTopPage && dates.Count > 0)
        {
            return dates;
        }

        if (targetMonth == previousMonth)
        {
            var previousMonthSnapshot = await TryMoveToPreviousMonthAsync(browser, snapshot, previousMonth, cancellationToken).ConfigureAwait(false);
            if (previousMonthSnapshot is not null)
            {
                dates = _parser.ParseMonthDates(previousMonthSnapshot, year, month);
                if (dates.Count > 0)
                {
                    return dates;
                }
            }
        }

        await browser.ClickAsync("過去レース結果検索", cancellationToken).ConfigureAwait(false);
        await browser.SelectOptionAsync("年", year.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await browser.SelectOptionAsync("月", month.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        await browser.ClickActionInSectionAsync("開催年月", "検索", cancellationToken).ConfigureAwait(false);

        snapshot = await GetMergedSnapshotAsync(browser, cancellationToken).ConfigureAwait(false);
        return _parser.ParseMonthDates(snapshot, year, month);
    }

    private static DateOnly GetCurrentMonth()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Jst);
        return new DateOnly(now.Year, now.Month, 1);
    }

    private static async Task<PageSnapshot> GetMergedSnapshotAsync(IWebBrowser browser, CancellationToken cancellationToken)
    {
        var snapshot = await browser.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: cancellationToken).ConfigureAwait(false);
        var links = await browser.GetLinksAsync(maxResults: 300, cancellationToken: cancellationToken).ConfigureAwait(false);
        var mergedLinks = snapshot.Links
            .Concat(links)
            .Where(link => !string.IsNullOrWhiteSpace(link.Url) || !string.IsNullOrWhiteSpace(link.Title))
            .GroupBy(link => $"{link.Url}|{link.Title}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var mergedSection = new PageSectionSnapshot(
            title: snapshot.Title,
            mainText: snapshot.MainText,
            headings: [snapshot.Title],
            links: mergedLinks,
            actions: snapshot.Actions.ToList(),
            tables: snapshot.Tables.ToList(),
            forms: snapshot.Forms.ToList(),
            images: snapshot.Images.ToList());

        return new PageSnapshot(snapshot.Url, snapshot.Title, [mergedSection]);
    }

    private static async Task<PageSnapshot?> TryMoveToPreviousMonthAsync(
        IWebBrowser browser,
        PageSnapshot currentSnapshot,
        DateOnly targetMonth,
        CancellationToken cancellationToken)
    {
        var candidate = FindMonthNavigationCandidate(currentSnapshot, targetMonth);
        if (candidate is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Url))
        {
            await browser.NavigateAsync(candidate.Url, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await browser.ClickAsync(candidate.Text, cancellationToken).ConfigureAwait(false);
        }

        return await GetMergedSnapshotAsync(browser, cancellationToken).ConfigureAwait(false);
    }

    private static MonthNavigationCandidate? FindMonthNavigationCandidate(PageSnapshot snapshot, DateOnly targetMonth)
    {
        var monthToken = $"{targetMonth.Month}月";

        var linkCandidate = snapshot.Links
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .Select(x => new MonthNavigationCandidate(x.Title, x.Url))
            .FirstOrDefault(x => x.Text.Contains(monthToken, StringComparison.Ordinal));
        if (linkCandidate is not null)
        {
            return linkCandidate;
        }

        var actionCandidate = snapshot.Actions
            .Select(x => new MonthNavigationCandidate(x.Text, null))
            .FirstOrDefault(x => x.Text.Contains(monthToken, StringComparison.Ordinal) || x.Text.Contains("前月", StringComparison.Ordinal));
        if (actionCandidate is not null)
        {
            return actionCandidate;
        }

        return snapshot.Links
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .Select(x => new MonthNavigationCandidate(x.Title, x.Url))
            .FirstOrDefault(x => x.Text.Contains("前月", StringComparison.Ordinal));
    }
}
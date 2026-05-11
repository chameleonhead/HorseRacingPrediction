using System.Globalization;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// JRA 公式サイトの成績ページをクリック遷移で探索し、
/// 指定開催日の成績 URL 一覧を返すスクレイピングエージェント。
/// <para>
/// このエージェントは成績 URL を「発見する」だけであり、各ページの詳細を読まない。
/// 実際の成績データの抽出は <see cref="Scrapers.Jra.JraRaceResultScraper"/> が担う。
/// </para>
/// </summary>
internal sealed class JraRaceResultUrlDiscoveryAgent
{
    private sealed record ResultClickCandidate(string Text, string? Url, int Score);

    public const string AgentName = "JraRaceResultUrlDiscoveryAgent";

    private const int MaxPageVisits = 20;
    private const int MaxDepth = 4;
    private readonly IWebBrowser _browser;
    private readonly ILogger<JraRaceResultUrlDiscoveryAgent> _logger;

    public JraRaceResultUrlDiscoveryAgent(IWebBrowser browser, ILogger<JraRaceResultUrlDiscoveryAgent>? logger = null)
    {
        _browser = browser;
        _logger = logger ?? NullLogger<JraRaceResultUrlDiscoveryAgent>.Instance;
    }

    public async Task<IReadOnlyList<JraRaceResultUrl>> DiscoverUrlsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("JRA result URL discovery started. RaceDate={RaceDate}", raceDate);

        var visitedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedTransitions = new HashSet<string>(StringComparer.Ordinal);
        var seenResultUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<JraRaceResultUrl>();

        await _browser.NavigateAsync("https://www.jra.go.jp/keiba/", cancellationToken);
        await ExploreByClickAsync(0, raceDate, visitedPages, visitedTransitions, seenResultUrls, results, cancellationToken);

        var ordered = results
            .OrderBy(r => r.RaceNumber ?? int.MaxValue)
            .ThenBy(r => r.Url, StringComparer.Ordinal)
            .ToList();

        _logger.LogInformation(
            "JRA result URL discovery completed. RaceDate={RaceDate} VisitedPages={VisitedPages} UniqueCandidates={UniqueCandidates} DateMatched={DateMatched}",
            raceDate,
            visitedPages.Count,
            seenResultUrls.Count,
            ordered.Count);

        if (ordered.Count == 0)
        {
            _logger.LogWarning(
                "JRA result URL discovery found no URLs for requested date. RaceDate={RaceDate}",
                raceDate);
        }

        return ordered;
    }

    private async Task ExploreByClickAsync(
        int depth,
        DateOnly raceDate,
        HashSet<string> visitedPages,
        HashSet<string> visitedTransitions,
        HashSet<string> seenResultUrls,
        List<JraRaceResultUrl> results,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > MaxDepth || visitedPages.Count >= MaxPageVisits)
        {
            return;
        }

        var snapshot = await _browser.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: cancellationToken);
        var extractedLinks = await _browser.GetLinksAsync(maxResults: 300, cancellationToken: cancellationToken);
        var effectiveSnapshot = snapshot with
        {
            Links = MergeLinks(snapshot.Links, extractedLinks, snapshot.Url),
        };

        var currentUrl = effectiveSnapshot.Url;
        if (!string.IsNullOrWhiteSpace(currentUrl)
            && !currentUrl.StartsWith("https://www.jra.go.jp/keiba", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentUrl))
        {
            visitedPages.Add(currentUrl);
        }

        _logger.LogInformation(
            "JRA discovery page. Depth={Depth} Url={Url} Headings={HeadingCount} Links={LinkCount} Actions={ActionCount}",
            depth,
            currentUrl,
            effectiveSnapshot.Headings.Count,
            effectiveSnapshot.Links.Count,
            effectiveSnapshot.Actions.Count);

        var (candidateFound, dateMatched) = CollectResultUrls(effectiveSnapshot, raceDate, seenResultUrls, results);
        if (candidateFound > 0)
        {
            _logger.LogInformation(
                "JRA discovery link scan. Depth={Depth} Url={Url} NewCandidates={NewCandidates} DateMatched={DateMatched}",
                depth,
                currentUrl,
                candidateFound,
                dateMatched);
        }

        var clickCandidates = BuildResultClickCandidates(effectiveSnapshot, raceDate);
        _logger.LogInformation(
            "JRA discovery click candidates. Depth={Depth} Url={Url} Candidates={CandidateCount} Top={TopCandidates}",
            depth,
            currentUrl,
            clickCandidates.Count,
            string.Join(" | ", clickCandidates.Take(5).Select(candidate => candidate.Text)));

        foreach (var candidate in clickCandidates)
        {
            var transitionKey = $"{currentUrl}|{NormalizeText(candidate.Url ?? candidate.Text)}";
            if (!visitedTransitions.Add(transitionKey))
            {
                continue;
            }

            var clicked = false;
            try
            {
                _logger.LogInformation(
                    "JRA discovery click attempt. Depth={Depth} Url={Url} Target={Target}",
                    depth,
                    currentUrl,
                    candidate.Text);

                if (!string.IsNullOrWhiteSpace(candidate.Url))
                {
                    await _browser.NavigateAsync(candidate.Url, cancellationToken);
                }
                else
                {
                    await _browser.ClickAsync(candidate.Text, cancellationToken);
                }

                clicked = true;

                var nextSnapshot = await _browser.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: cancellationToken);
                var (nextCandidateFound, nextDateMatched) = CollectResultUrls(nextSnapshot, raceDate, seenResultUrls, results);
                _logger.LogInformation(
                    "JRA discovery click success. Depth={Depth} From={FromUrl} To={ToUrl} NewCandidates={NewCandidates} DateMatched={DateMatched}",
                    depth,
                    currentUrl,
                    nextSnapshot.Url,
                    nextCandidateFound,
                    nextDateMatched);

                await ExploreByClickAsync(
                    depth + 1,
                    raceDate,
                    visitedPages,
                    visitedTransitions,
                    seenResultUrls,
                    results,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "JRA discovery click failed. Depth={Depth} Url={Url} Target={Target} TargetUrl={TargetUrl}",
                    depth,
                    currentUrl,
                    candidate.Text,
                    candidate.Url);
            }
            finally
            {
                if (clicked)
                {
                    try { await _browser.GoBackAsync(cancellationToken); } catch { }
                }
            }
        }
    }

    private static (int CandidateFound, int DateMatched) CollectResultUrls(
        PageSnapshot snapshot,
        DateOnly raceDate,
        HashSet<string> seenResultUrls,
        List<JraRaceResultUrl> results)
    {
        var candidateFound = 0;
        var dateMatched = 0;

        foreach (var link in snapshot.Links)
        {
            var absoluteUrl = NormalizeAbsoluteUrl(link.Url, snapshot.Url);
            if (string.IsNullOrWhiteSpace(absoluteUrl)
                || !absoluteUrl.Contains("CNAME=pw01skd0203_", StringComparison.OrdinalIgnoreCase)
                || !seenResultUrls.Add(absoluteUrl))
            {
                continue;
            }

            candidateFound++;

            var parsed = JraRaceResultUrl.ParseFromUrl(absoluteUrl, racecourse: null);
            if (parsed.RaceDate is null || parsed.RaceDate == raceDate)
            {
                results.Add(parsed);
                dateMatched++;
            }
        }

        return (candidateFound, dateMatched);
    }

    private static IReadOnlyList<ResultClickCandidate> BuildResultClickCandidates(PageSnapshot snapshot, DateOnly raceDate)
    {
        var dayText = $"{raceDate.Month}月{raceDate.Day}日";
        var monthText = $"{raceDate.Month}月";
        var yearText = raceDate.Year.ToString(CultureInfo.InvariantCulture);
        var currentUrl = snapshot.Url ?? string.Empty;

        var actionCandidates = snapshot.Actions
            .Select(action => action.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => new ResultClickCandidate(
                text!.Trim(),
                null,
                ScoreClickCandidate(text, null, currentUrl, dayText, monthText, yearText, raceDate.Month)));

        var linkCandidates = snapshot.Links
            .Where(link => !string.IsNullOrWhiteSpace(link.Title))
            .Select(link =>
            {
                var normalizedUrl = NormalizeAbsoluteUrl(link.Url, currentUrl);
                return new ResultClickCandidate(
                    link.Title!.Trim(),
                    normalizedUrl,
                    ScoreClickCandidate(link.Title!, normalizedUrl, currentUrl, dayText, monthText, yearText, raceDate.Month));
            });

        return actionCandidates
            .Concat(linkCandidates)
            .GroupBy(candidate => candidate.Text, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Url is null ? 1 : 0)
                .First())
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Text.Length)
            .Take(15)
            .ToList();
    }

    private static int ScoreClickCandidate(
        string text,
        string? url,
        string currentUrl,
        string dayText,
        string monthText,
        string yearText,
        int raceMonth)
    {
        var normalized = NormalizeText(text);
        var normalizedDayText = NormalizeText(dayText);
        var normalizedMonthText = NormalizeText(monthText);
        var normalizedCurrentUrl = currentUrl ?? string.Empty;
        var score = 0;

        if (normalized.Contains(normalizedDayText, StringComparison.Ordinal)) score += 400;
        if (normalized.Equals(normalizedMonthText, StringComparison.Ordinal)) score += 320;
        if (normalized.Contains(normalizedMonthText, StringComparison.Ordinal)) score += 220;
        if (normalized.Equals(yearText, StringComparison.Ordinal) || normalized.Contains($"{yearText}年", StringComparison.Ordinal)) score += 240;
        if (normalized.Contains("開催日程", StringComparison.Ordinal)) score += 300;
        if (normalized.Contains("カレンダー", StringComparison.Ordinal)) score += 220;
        if (normalized.Contains("開催場別", StringComparison.Ordinal)) score += 160;
        if (normalized.Contains("レース結果", StringComparison.Ordinal)) score += 120;
        if (normalized.Contains("結果", StringComparison.Ordinal)) score += 60;
        if (normalized.Contains("成績", StringComparison.Ordinal)) score += 50;
        if (normalized.Contains("開催", StringComparison.Ordinal)) score += 45;
        if (normalized.Contains("今週", StringComparison.Ordinal)) score += 25;
        if (normalized.Contains(yearText, StringComparison.Ordinal)) score += 25;

        if (normalized.Contains("払戻", StringComparison.Ordinal)) score -= 80;
        if (normalized.Contains("税", StringComparison.Ordinal)) score -= 100;
        if (normalized.Contains("支払", StringComparison.Ordinal)) score -= 100;

        if (!string.IsNullOrWhiteSpace(url))
        {
            if (url.Contains("/keiba/calendar", StringComparison.OrdinalIgnoreCase)) score += 260;
            if (url.Contains("/datafile/seiseki", StringComparison.OrdinalIgnoreCase)) score += 220;
            if (url.Contains("/JRADB/accessD", StringComparison.OrdinalIgnoreCase)) score += 240;
            if (url.Contains("/kouza/haraimodoshi", StringComparison.OrdinalIgnoreCase)) score -= 200;
            if (url.Contains("/company/social", StringComparison.OrdinalIgnoreCase)) score -= 200;
        }

        if (normalizedCurrentUrl.Contains("/keiba/calendar", StringComparison.OrdinalIgnoreCase))
        {
            if (normalized.Equals(normalizedMonthText, StringComparison.Ordinal) || normalized.Contains($"{yearText}年{raceMonth}月", StringComparison.Ordinal))
            {
                score += 180;
            }
        }

        if (normalizedCurrentUrl.Contains("/datafile/seiseki", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("レース結果", StringComparison.Ordinal))
        {
            score += 120;
        }

        return score;
    }

    private static string NormalizeText(string text)
        => text.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static IReadOnlyList<SearchResultLink> MergeLinks(
        IReadOnlyList<SearchResultLink> snapshotLinks,
        IReadOnlyList<SearchResultLink> extractedLinks,
        string? baseUrl)
    {
        return snapshotLinks
            .Concat(extractedLinks)
            .Where(link => !string.IsNullOrWhiteSpace(link.Url) || !string.IsNullOrWhiteSpace(link.Title))
            .GroupBy(
                link => NormalizeAbsoluteUrl(link.Url, baseUrl) ?? NormalizeText(link.Title ?? string.Empty),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string? NormalizeAbsoluteUrl(string? candidate, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            if (string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return absolute.AbsoluteUri;
            }
        }

        if (!string.IsNullOrWhiteSpace(baseUrl)
            && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            && Uri.TryCreate(baseUri, candidate, out var resolved))
        {
            return resolved.AbsoluteUri;
        }

        return null;
    }
}
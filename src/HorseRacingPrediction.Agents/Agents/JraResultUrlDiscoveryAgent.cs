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
public sealed class JraResultUrlDiscoveryAgent
{
    public const string AgentName = "JraResultUrlDiscoveryAgent";

    private const int MaxPageVisits = 20;
    private const int MaxDepth = 4;
    private readonly IWebBrowser _browser;
    private readonly ILogger<JraResultUrlDiscoveryAgent> _logger;

    public JraResultUrlDiscoveryAgent(IWebBrowser browser, ILogger<JraResultUrlDiscoveryAgent>? logger = null)
    {
        _browser = browser;
        _logger = logger ?? NullLogger<JraResultUrlDiscoveryAgent>.Instance;
    }

    /// <summary>
    /// 指定した週末の開催日に対応する成績 URL 一覧を返す。
    /// </summary>
    /// <param name="raceDate">対象の開催日付</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>発見された成績 URL の一覧</returns>
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

        _logger.LogInformation(
            "JRA discovery page. Depth={Depth} Url={Url} Headings={HeadingCount} Links={LinkCount} Actions={ActionCount}",
            depth,
            currentUrl,
            snapshot.Headings.Count,
            snapshot.Links.Count,
            snapshot.Actions.Count);

        var (candidateFound, dateMatched) = CollectResultUrls(snapshot, raceDate, seenResultUrls, results);
        if (candidateFound > 0)
        {
            _logger.LogInformation(
                "JRA discovery link scan. Depth={Depth} Url={Url} NewCandidates={NewCandidates} DateMatched={DateMatched}",
                depth,
                currentUrl,
                candidateFound,
                dateMatched);
        }

        var clickCandidates = BuildResultClickCandidates(snapshot, raceDate);
        _logger.LogInformation(
            "JRA discovery click candidates. Depth={Depth} Url={Url} Candidates={CandidateCount} Top={TopCandidates}",
            depth,
            currentUrl,
            clickCandidates.Count,
            string.Join(" | ", clickCandidates.Take(5)));

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
                _logger.LogInformation(
                    "JRA discovery click attempt. Depth={Depth} Url={Url} Target={Target}",
                    depth,
                    currentUrl,
                    clickText);
                await _browser.ClickAsync(clickText, cancellationToken);
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
            catch
            {
                _logger.LogInformation(
                    "JRA discovery click failed. Depth={Depth} Url={Url} Target={Target}",
                    depth,
                    currentUrl,
                    clickText);
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

    private static IReadOnlyList<string> BuildResultClickCandidates(PageSnapshot snapshot, DateOnly raceDate)
    {
        var dayText = $"{raceDate.Month}月{raceDate.Day}日";
        var monthText = $"{raceDate.Month}月";
        var yearText = raceDate.Year.ToString(CultureInfo.InvariantCulture);

        return snapshot.Actions.Select(a => a.Text)
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
    }

    private static int ScoreClickCandidate(string text, string dayText, string monthText, string yearText)
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

    private static string NormalizeText(string text)
        => text.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static string? NormalizeAbsoluteUrl(string? candidate, string? baseUrl)
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
}

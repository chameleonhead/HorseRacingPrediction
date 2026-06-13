using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Scrapers.Jra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// JRA 公式サイトのメニューからレース結果画面へ遷移し、
/// 画面上の開催ボタンと過去レース結果検索だけを使って
/// 指定開催日の成績 URL 一覧を返す discovery agent。
/// </summary>
internal sealed class JraRaceResultUrlDiscoveryAgent
{
    private sealed record MeetingCandidate(string Text, string? Url);

    private static readonly string[] RacecourseNames =
    [
        "札幌",
        "函館",
        "福島",
        "新潟",
        "東京",
        "中山",
        "中京",
        "京都",
        "阪神",
        "小倉",
    ];

    private static readonly Regex MeetingTextRegex =
        new(@"\d+回(?:札幌|函館|福島|新潟|東京|中山|中京|京都|阪神|小倉)\d+日", RegexOptions.Compiled);

    private static readonly Regex DaySectionRegex =
        new(@"\d{1,2}月\d{1,2}日(?:（(?:月曜|火曜|水曜|木曜|金曜|土曜|日曜)）)?", RegexOptions.Compiled);

    public const string AgentName = "JraRaceResultUrlDiscoveryAgent";

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

        await _browser.NavigateAsync("https://www.jra.go.jp/keiba/", cancellationToken);
        await _browser.ClickAsync("レース結果", cancellationToken);

        var selectionSnapshot = await GetMergedSnapshotAsync(cancellationToken);
        if (ShouldUseHistoricalSearch(selectionSnapshot, raceDate))
        {
            _logger.LogInformation(
                "JRA result URL discovery switching to historical search. RaceDate={RaceDate} CurrentUrl={CurrentUrl}",
                raceDate,
                selectionSnapshot.Url);

            await _browser.ClickAsync("過去レース結果検索", cancellationToken);
            await _browser.SelectOptionAsync("年", raceDate.Year.ToString(CultureInfo.InvariantCulture), cancellationToken);
            await _browser.SelectOptionAsync("月", raceDate.Month.ToString(CultureInfo.InvariantCulture), cancellationToken);
            await _browser.ClickActionInSectionAsync("開催年月", "検索", cancellationToken);
            selectionSnapshot = await GetMergedSnapshotAsync(cancellationToken);
        }

        var discovered = await CollectMeetingResultUrlsAsync(selectionSnapshot, raceDate, cancellationToken);
        var ordered = discovered
            .GroupBy(BuildRaceIdentityKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(result => result.RaceNumber ?? int.MaxValue)
            .ThenBy(result => result.Url, StringComparer.Ordinal)
            .ToList();

        _logger.LogInformation(
            "JRA result URL discovery completed. RaceDate={RaceDate} DiscoveredCount={DiscoveredCount}",
            raceDate,
            ordered.Count);

        if (ordered.Count == 0)
        {
            _logger.LogWarning("JRA result URL discovery found no URLs. RaceDate={RaceDate}", raceDate);
        }

        return ordered;
    }

    private async Task<IReadOnlyList<JraRaceResultUrl>> CollectMeetingResultUrlsAsync(
        PageSnapshot selectionSnapshot,
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        var currentPageResults = CollectResultUrls(selectionSnapshot, raceDate);
        if (currentPageResults.Count > 0 && ContainsFullRaceDate(selectionSnapshot, raceDate))
        {
            _logger.LogInformation(
                "JRA result URL discovery collected URLs directly from current page. RaceDate={RaceDate} ResultCount={ResultCount} CurrentUrl={CurrentUrl}",
                raceDate,
                currentPageResults.Count,
                selectionSnapshot.Url);
            return currentPageResults;
        }

        var meetingCandidates = BuildMeetingCandidates(selectionSnapshot, raceDate);
        var discovered = new List<JraRaceResultUrl>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "JRA result URL discovery meeting candidates. RaceDate={RaceDate} CandidateCount={CandidateCount} CurrentUrl={CurrentUrl}",
            raceDate,
            meetingCandidates.Count,
            selectionSnapshot.Url);

        foreach (var candidate in meetingCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var clicked = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(candidate.Text))
                {
                    await _browser.ClickAsync(candidate.Text, cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(candidate.Url))
                {
                    await _browser.NavigateAsync(candidate.Url, cancellationToken);
                }
                else
                {
                    continue;
                }

                clicked = true;

                var raceSelectionSnapshot = await GetMergedSnapshotAsync(cancellationToken);
                if (!ContainsFullRaceDate(raceSelectionSnapshot, raceDate))
                {
                    _logger.LogDebug(
                        "JRA result URL discovery skipped meeting candidate due to date mismatch. RaceDate={RaceDate} Candidate={Candidate} CurrentUrl={CurrentUrl}",
                        raceDate,
                        candidate.Text,
                        raceSelectionSnapshot.Url);
                    continue;
                }

                foreach (var resultUrl in CollectResultUrls(raceSelectionSnapshot, raceDate))
                {
                    if (seenUrls.Add(resultUrl.Url))
                    {
                        discovered.Add(resultUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "JRA result URL discovery failed to inspect meeting candidate. RaceDate={RaceDate} Candidate={Candidate}",
                    raceDate,
                    candidate.Text);
            }
            finally
            {
                if (clicked)
                {
                    try
                    {
                        await _browser.GoBackAsync(cancellationToken);
                    }
                    catch
                    {
                    }
                }
            }
        }

        return discovered;
    }

    private static string BuildRaceIdentityKey(JraRaceResultUrl url)
    {
        var raceDate = url.RaceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown-date";
        var racecourse = !string.IsNullOrWhiteSpace(url.Racecourse)
            ? url.Racecourse
            : url.RacecourseCode ?? "unknown-course";
        var raceNumber = url.RaceNumber?.ToString("D2", CultureInfo.InvariantCulture) ?? "00";
        return $"{raceDate}|{racecourse}|{raceNumber}";
    }

    private static IReadOnlyList<MeetingCandidate> BuildMeetingCandidates(PageSnapshot snapshot, DateOnly raceDate)
    {
        var scopedMeetingTexts = ExtractScopedMeetingTexts(snapshot, raceDate);
        var allowedMeetingTexts = scopedMeetingTexts.Count > 0
            ? new HashSet<string>(scopedMeetingTexts, StringComparer.Ordinal)
            : null;

        var linkCandidates = snapshot.Links
            .Select(link => new MeetingCandidate(
                NormalizeText(link.Title ?? string.Empty),
                NormalizeAbsoluteUrl(link.Url, snapshot.Url)))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text))
            .Where(candidate => MeetingTextRegex.IsMatch(candidate.Text))
            .Where(candidate => allowedMeetingTexts is null || allowedMeetingTexts.Contains(candidate.Text))
            .ToList();

        var textCandidates = (allowedMeetingTexts is null
                ? EnumerateSnapshotText(snapshot)
                    .SelectMany(text => MeetingTextRegex.Matches(NormalizeText(text)).Select(match => match.Value))
                : allowedMeetingTexts)
            .Select(text => new MeetingCandidate(text, null));

        return linkCandidates
            .Concat(textCandidates)
            .GroupBy(candidate => candidate.Text, StringComparer.Ordinal)
            .Select(group => group.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.Url)) ?? group.First())
            .ToList();
    }

    private static IReadOnlyList<string> ExtractScopedMeetingTexts(PageSnapshot snapshot, DateOnly raceDate)
    {
        var scopedTexts = EnumerateSnapshotText(snapshot)
            .Select(text => ExtractTargetDaySectionText(text, raceDate))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .SelectMany(text => MeetingTextRegex.Matches(text!).Select(match => match.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return scopedTexts;
    }

    private static string? ExtractTargetDaySectionText(string text, DateOnly raceDate)
    {
        var normalizedText = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return null;
        }

        var targetDayToken = NormalizeText($"{raceDate.Month}月{raceDate.Day}日");
        var startIndex = normalizedText.IndexOf(targetDayToken, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return null;
        }

        var nextSectionMatch = DaySectionRegex.Matches(normalizedText)
            .FirstOrDefault(match => match.Index > startIndex);
        var endIndex = nextSectionMatch?.Index ?? normalizedText.Length;
        if (endIndex <= startIndex)
        {
            endIndex = normalizedText.Length;
        }

        return normalizedText[startIndex..endIndex];
    }

    private static IReadOnlyList<JraRaceResultUrl> CollectResultUrls(PageSnapshot snapshot, DateOnly raceDate)
    {
        return snapshot.Links
            .Select(link => NormalizeAbsoluteUrl(link.Url, snapshot.Url))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => JraRaceResultUrl.ParseFromUrl(url!))
            .Where(result => result.RaceDate == raceDate)
            .Where(result => result.RaceNumber is not null)
            .GroupBy(result => result.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private async Task<PageSnapshot> GetMergedSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _browser.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: cancellationToken);
        var links = await _browser.GetLinksAsync(maxResults: 300, cancellationToken: cancellationToken);
        var mergedLinks = snapshot.Links
            .Concat(links)
            .Where(link => !string.IsNullOrWhiteSpace(link.Url) || !string.IsNullOrWhiteSpace(link.Title))
            .GroupBy(
                link => NormalizeAbsoluteUrl(link.Url, snapshot.Url) ?? NormalizeText(link.Title ?? string.Empty),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var mergedSection = new PageSectionSnapshot(
            title: snapshot.Title,
            mainText: snapshot.MainText,
            links: mergedLinks,
            actions: snapshot.Actions.ToList(),
            tables: snapshot.Tables.ToList(),
            headings: snapshot.Headings.ToList(),
            forms: snapshot.Forms.ToList(),
            images: snapshot.Images.ToList());

        return new PageSnapshot(snapshot.Url, snapshot.Title, [mergedSection]);
    }

    private static bool ShouldUseHistoricalSearch(PageSnapshot snapshot, DateOnly raceDate)
    {
        if (raceDate.Year != DateTime.Today.Year)
        {
            return true;
        }

        return !ContainsDayToken(snapshot, raceDate);
    }

    private static bool ContainsDayToken(PageSnapshot snapshot, DateOnly raceDate)
    {
        var dayToken = NormalizeText($"{raceDate.Month}月{raceDate.Day}日");
        return EnumerateSnapshotText(snapshot)
            .Any(text => NormalizeText(text).Contains(dayToken, StringComparison.Ordinal));
    }

    private static bool ContainsFullRaceDate(PageSnapshot snapshot, DateOnly raceDate)
    {
        var fullDateToken = NormalizeText($"{raceDate.Year}年{raceDate.Month}月{raceDate.Day}日");
        return EnumerateSnapshotText(snapshot)
            .Any(text => NormalizeText(text).Contains(fullDateToken, StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateSnapshotText(PageSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Title))
        {
            yield return snapshot.Title;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.MainText))
        {
            yield return snapshot.MainText;
        }

        foreach (var heading in snapshot.Headings)
        {
            if (!string.IsNullOrWhiteSpace(heading))
            {
                yield return heading;
            }
        }

        foreach (var link in snapshot.Links)
        {
            if (!string.IsNullOrWhiteSpace(link.Title))
            {
                yield return link.Title!;
            }
        }
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

        var trimmedCandidate = candidate.Trim();
        if (trimmedCandidate.StartsWith('#')
            || trimmedCandidate.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Uri.TryCreate(trimmedCandidate, UriKind.Absolute, out var absolute))
        {
            if (string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return absolute.AbsoluteUri;
            }
        }

        if (!string.IsNullOrWhiteSpace(baseUrl)
            && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            && Uri.TryCreate(baseUri, trimmedCandidate, out var resolved))
        {
            return resolved.AbsoluteUri;
        }

        return null;
    }
}
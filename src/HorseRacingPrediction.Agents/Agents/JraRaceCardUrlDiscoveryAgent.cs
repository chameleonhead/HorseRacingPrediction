using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// JRA 公式サイトの今週ページと開催ページを巡回し、
/// 指定日の出馬表 URL 一覧を返す discovery agent。
/// </summary>
internal sealed class JraRaceCardUrlDiscoveryAgent
{
    private const string KeibaMenuUrl = "https://www.jra.go.jp/keiba/";
    private const string ThisWeekUrl = "https://www.jra.go.jp/keiba/thisweek/";

    private static readonly Regex MeetingLinkDateRegex = new(@"20\d{6}", RegexOptions.Compiled);
    private static readonly Regex RaceNumberRegex = new(@"(?<number>\d{1,2})R", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SnapshotDateRegex = new(@"(?<year>\d{4})年(?<month>\d{1,2})月(?<day>\d{1,2})日", RegexOptions.Compiled);

    private readonly IWebBrowser _browser;
    private readonly ILogger<JraRaceCardUrlDiscoveryAgent> _logger;

    public JraRaceCardUrlDiscoveryAgent(IWebBrowser browser, ILogger<JraRaceCardUrlDiscoveryAgent>? logger = null)
    {
        _browser = browser;
        _logger = logger ?? NullLogger<JraRaceCardUrlDiscoveryAgent>.Instance;
    }

    public async Task<IReadOnlyList<JraRaceCardUrl>> DiscoverUrlsAsync(
        DateOnly weekendDate,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("JRA race card URL discovery started. RaceDate={RaceDate}", weekendDate);

        await _browser.NavigateAsync(ThisWeekUrl, cancellationToken).ConfigureAwait(false);

        var thisWeekSnapshot = await GetMergedSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!ContainsExactRequestedDate(thisWeekSnapshot, weekendDate))
        {
            _logger.LogWarning(
                "JRA race card URL discovery cannot use current-week route for requested date. RaceDate={RaceDate} CurrentUrl={CurrentUrl}",
                weekendDate,
                thisWeekSnapshot.Url);

            return await DiscoverFromMenuAsync(weekendDate, cancellationToken).ConfigureAwait(false);
        }

        var discovered = new List<JraRaceCardUrl>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cardUrl in CollectRaceCardUrls(thisWeekSnapshot, weekendDate))
        {
            if (seenUrls.Add(cardUrl.Url))
            {
                discovered.Add(cardUrl);
            }
        }

        foreach (var meetingUrl in BuildMeetingUrls(thisWeekSnapshot, weekendDate))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _browser.NavigateAsync(meetingUrl, cancellationToken).ConfigureAwait(false);
                var meetingSnapshot = await GetMergedSnapshotAsync(cancellationToken).ConfigureAwait(false);

                foreach (var cardUrl in CollectRaceCardUrls(meetingSnapshot, weekendDate))
                {
                    if (seenUrls.Add(cardUrl.Url))
                    {
                        discovered.Add(cardUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "JRA race card URL discovery failed to inspect meeting page. RaceDate={RaceDate} MeetingUrl={MeetingUrl}",
                    weekendDate,
                    meetingUrl);
            }
        }

        if (discovered.Count == 0)
        {
            foreach (var cardUrl in await DiscoverFromMenuAsync(weekendDate, cancellationToken).ConfigureAwait(false))
            {
                if (seenUrls.Add(cardUrl.Url))
                {
                    discovered.Add(cardUrl);
                }
            }
        }

        var ordered = discovered
            .OrderBy(url => url.RaceNumber ?? int.MaxValue)
            .ThenBy(url => url.Url, StringComparer.Ordinal)
            .ToList();

        _logger.LogInformation(
            "JRA race card URL discovery completed. RaceDate={RaceDate} DiscoveredCount={DiscoveredCount}",
            weekendDate,
            ordered.Count);

        return ordered;
    }

    private async Task<IReadOnlyList<JraRaceCardUrl>> DiscoverFromMenuAsync(
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        var discovered = new List<JraRaceCardUrl>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await _browser.NavigateAsync(KeibaMenuUrl, cancellationToken).ConfigureAwait(false);
            await _browser.ClickAsync("出馬表", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "JRA race card URL discovery fallback failed to open holdings page. RaceDate={RaceDate}",
                requestedDate);
            return discovered;
        }

        var holdingsSnapshot = await GetMergedSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var holdingsUrl = _browser.CurrentUrl;

        if (!ContainsExactRequestedDate(holdingsSnapshot, requestedDate))
        {
            _logger.LogWarning(
                "JRA race card URL discovery could not find requested date on holdings page. RaceDate={RaceDate} CurrentUrl={CurrentUrl}",
                requestedDate,
                holdingsSnapshot.Url);
            return discovered;
        }

        foreach (var cardUrl in CollectRaceCardUrls(holdingsSnapshot, requestedDate))
        {
            if (seenUrls.Add(cardUrl.Url))
            {
                discovered.Add(cardUrl);
            }
        }

        foreach (var holdingLabel in ExtractHoldingLabels(holdingsSnapshot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!string.IsNullOrWhiteSpace(holdingsUrl)
                    && !string.Equals(_browser.CurrentUrl, holdingsUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await _browser.NavigateAsync(holdingsUrl, cancellationToken).ConfigureAwait(false);
                }

                await _browser.ClickAsync(holdingLabel, cancellationToken).ConfigureAwait(false);
                var raceListSnapshot = await GetMergedSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (!ContainsExactRequestedDate(raceListSnapshot, requestedDate))
                {
                    continue;
                }

                var raceListUrl = _browser.CurrentUrl;
                var directUrls = CollectRaceCardUrls(raceListSnapshot, requestedDate);
                if (directUrls.Count == 0)
                {
                    directUrls = await CollectRaceCardUrlsByClickingRaceNumbersAsync(
                        requestedDate,
                        holdingLabel,
                        holdingsUrl,
                        raceListUrl,
                        raceListSnapshot,
                        cancellationToken).ConfigureAwait(false);
                }

                foreach (var cardUrl in directUrls)
                {
                    if (seenUrls.Add(cardUrl.Url))
                    {
                        discovered.Add(cardUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "JRA race card URL discovery fallback failed to inspect holding. RaceDate={RaceDate} HoldingLabel={HoldingLabel}",
                    requestedDate,
                    holdingLabel);
            }
        }

        return discovered;
    }

    private async Task<IReadOnlyList<JraRaceCardUrl>> CollectRaceCardUrlsByClickingRaceNumbersAsync(
        DateOnly requestedDate,
        string holdingLabel,
        string? holdingsUrl,
        string? raceListUrl,
        PageSnapshot raceListSnapshot,
        CancellationToken cancellationToken)
    {
        var discovered = new List<JraRaceCardUrl>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackRacecourse = ExtractRacecourse(raceListSnapshot);

        for (var raceNumber = 1; raceNumber <= 12; raceNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var clickLabel in BuildRaceNumberClickCandidates(raceNumber))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(raceListUrl)
                        && !string.Equals(_browser.CurrentUrl, raceListUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(holdingsUrl))
                        {
                            await _browser.NavigateAsync(holdingsUrl, cancellationToken).ConfigureAwait(false);
                            await _browser.ClickAsync(holdingLabel, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await _browser.NavigateAsync(raceListUrl, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    await _browser.ClickAsync(clickLabel, cancellationToken).ConfigureAwait(false);
                    var cardSnapshot = await GetMergedSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    var cardUrl = CreateRaceCardUrlFromCurrentPage(cardSnapshot, _browser.CurrentUrl, fallbackRacecourse, requestedDate, raceNumber);
                    if (cardUrl is not null && seenUrls.Add(cardUrl.Url))
                    {
                        discovered.Add(cardUrl);
                    }

                    break;
                }
                catch
                {
                }
            }
        }

        return discovered;
    }

    private static IReadOnlyList<string> BuildMeetingUrls(PageSnapshot snapshot, DateOnly requestedDate)
    {
        return snapshot.Links
            .Select(link => NormalizeAbsoluteUrl(link.Url, snapshot.Url))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .Where(url => url.Contains("pw01dde", StringComparison.OrdinalIgnoreCase))
            .Where(url => TryExtractLinkedDate(url, out var linkedDate) && linkedDate == requestedDate)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<JraRaceCardUrl> CollectRaceCardUrls(PageSnapshot snapshot, DateOnly requestedDate)
    {
        var racecourse = ExtractRacecourse(snapshot);
        var snapshotDate = ExtractSnapshotDate(snapshot);

        return snapshot.Links
            .Select(link => CreateRaceCardUrl(link, snapshot.Url, racecourse, snapshotDate, requestedDate))
            .Where(url => url is not null)
            .Select(url => url!)
            .GroupBy(url => url.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static JraRaceCardUrl? CreateRaceCardUrl(
        SearchResultLink link,
        string? baseUrl,
        string? fallbackRacecourse,
        DateOnly? snapshotDate,
        DateOnly requestedDate)
    {
        var normalizedUrl = NormalizeAbsoluteUrl(link.Url, baseUrl);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return null;
        }

        if (normalizedUrl.Contains("pw01sde0203_", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = JraRaceCardUrl.ParseFromUrl(normalizedUrl, fallbackRacecourse);
            return parsed.RaceDate == requestedDate ? parsed : null;
        }

        if (!normalizedUrl.Contains("/syutsuba", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (snapshotDate is null || snapshotDate != requestedDate)
        {
            return null;
        }

        var raceNumber = ExtractRaceNumber(link.Title) ?? ExtractRaceNumber(normalizedUrl);
        return new JraRaceCardUrl(normalizedUrl, fallbackRacecourse, null, requestedDate, raceNumber);
    }

    private static JraRaceCardUrl? CreateRaceCardUrlFromCurrentPage(
        PageSnapshot snapshot,
        string? currentUrl,
        string? fallbackRacecourse,
        DateOnly requestedDate,
        int raceNumber)
    {
        var normalizedUrl = NormalizeAbsoluteUrl(currentUrl ?? snapshot.Url, snapshot.Url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return null;
        }

        if (normalizedUrl.Contains("pw01sde0203_", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = JraRaceCardUrl.ParseFromUrl(normalizedUrl, fallbackRacecourse ?? ExtractRacecourse(snapshot));
            return parsed.RaceDate == requestedDate ? parsed : null;
        }

        if (!normalizedUrl.Contains("/syutsuba", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var snapshotDate = ExtractSnapshotDate(snapshot);
        if (snapshotDate != requestedDate)
        {
            return null;
        }

        return new JraRaceCardUrl(normalizedUrl, fallbackRacecourse ?? ExtractRacecourse(snapshot), null, requestedDate, raceNumber);
    }

    private static int? ExtractRaceNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = RaceNumberRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static IReadOnlyList<string> BuildRaceNumberClickCandidates(int raceNumber)
    {
        var baseNumber = raceNumber.ToString(CultureInfo.InvariantCulture);

        return new[]
        {
            $"{baseNumber}レース",
            $"第{baseNumber}レース",
            $"{baseNumber}R",
            $"{baseNumber}Ｒ",
            baseNumber,
        }
        .Distinct(StringComparer.Ordinal)
        .ToList();
    }

    private static string? ExtractRacecourse(PageSnapshot snapshot)
    {
        string[] knownRacecourses = ["札幌", "函館", "福島", "新潟", "東京", "中山", "中京", "京都", "阪神", "小倉"];

        foreach (var text in EnumerateSnapshotText(snapshot))
        {
            foreach (var racecourse in knownRacecourses)
            {
                if (text.Contains(racecourse, StringComparison.Ordinal))
                {
                    return racecourse;
                }
            }
        }

        return null;
    }

    private static DateOnly? ExtractSnapshotDate(PageSnapshot snapshot)
    {
        foreach (var text in EnumerateSnapshotText(snapshot))
        {
            var match = SnapshotDateRegex.Match(text);
            if (!match.Success)
            {
                continue;
            }

            if (int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                && int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                && int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            {
                try
                {
                    return new DateOnly(year, month, day);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractHoldingLabels(PageSnapshot snapshot)
    {
        return JraPageParserText.ExtractHoldingEntries(snapshot)
            .Select(entry => entry.Label)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool ContainsExactRequestedDate(PageSnapshot snapshot, DateOnly date)
    {
        var fullDateText = $"{date.Year}年{date.Month}月{date.Day}日";

        return EnumerateSnapshotText(snapshot)
            .Any(text => text.Contains(fullDateText, StringComparison.Ordinal));
    }

    private async Task<PageSnapshot> GetMergedSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _browser.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: cancellationToken).ConfigureAwait(false);
        var links = await _browser.GetLinksAsync(maxResults: 300, cancellationToken: cancellationToken).ConfigureAwait(false);

        return snapshot with
        {
            Links = snapshot.Links
                .Concat(links)
                .Where(link => !string.IsNullOrWhiteSpace(link.Url) || !string.IsNullOrWhiteSpace(link.Title))
                .GroupBy(
                    link => NormalizeAbsoluteUrl(link.Url, snapshot.Url) ?? NormalizeText(link.Title),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
        };
    }

    private static bool TryExtractLinkedDate(string url, out DateOnly linkedDate)
    {
        linkedDate = default;
        var match = MeetingLinkDateRegex.Match(url);
        if (!match.Success)
        {
            return false;
        }

        return DateOnly.TryParseExact(match.Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out linkedDate);
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
                yield return link.Title;
            }
        }
    }

    private static string NormalizeText(string? text)
        => (text ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
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
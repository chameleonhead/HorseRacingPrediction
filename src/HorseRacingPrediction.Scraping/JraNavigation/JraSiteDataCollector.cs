using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed class JraSiteDataCollector : IAsyncDisposable
{
    private const string EntryUrl = "https://www.jra.go.jp/keiba/";

    private static readonly Regex HoldingLabelRegex = new(
        @"\d+回(東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)\d+日",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FullDateRegex = new(
        @"(?<year>\d{4})\s*年\s*(?<month>\d{1,2})\s*月\s*(?<day>\d{1,2})\s*日",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MonthDayRegex = new(
        @"(?<!\d)(?<month>\d{1,2})\s*月\s*(?<day>\d{1,2})\s*日",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CompactDateRegex = new(
        @"(?<!\d)(?<date>20\d{6})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HeadingDateRegex = new(
        @"(?<month>\d{1,2})月(?<day>\d{1,2})日（[^）]+）",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private PlaywrightWebBrowser? _browser;
    private readonly JraSessionMemory _memory;
    private readonly JraNavigationPlanner _planner;
    private readonly JraExtractorRegistry _registry;
    private bool _disposed;

    private JraSiteDataCollector(PlaywrightWebBrowser browser, JraSessionMemory memory,
        JraNavigationPlanner planner, JraExtractorRegistry registry)
    {
        _browser  = browser;
        _memory   = memory;
        _planner  = planner;
        _registry = registry;
    }

    public static async Task<JraSiteDataCollector> CreateAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var browser = await PlaywrightWebBrowser.CreateAsync();
        var memory  = new JraSessionMemory();
        var planner = new JraNavigationPlanner();
        var registry = new JraExtractorRegistry(
        [
            new JraRaceCardExtractor(),
            new JraOddsExtractor(),
            new JraRaceResultExtractor(),
            new JraProfileExtractor(),
        ]);
        return new JraSiteDataCollector(browser, memory, planner, registry);
    }

    public JraPageKind CurrentPageKind => _memory.CurrentPageKind;
    public string? CurrentUrl => _browser?.CurrentUrl;

    public async Task<JraExtractionEnvelope<JraRaceCardData>> RequestRaceCardAsync(
        DateOnly date, string racecourse, int raceNumber, CancellationToken cancellationToken = default)
        => await RequestRacePageTypedAsync<JraRaceCardData>(date, racecourse, raceNumber, JraPageKind.RaceCard, cancellationToken);

    public async Task<JraExtractionEnvelope<JraOddsResult>> RequestOddsAsync(
        DateOnly date, string racecourse, int raceNumber, CancellationToken cancellationToken = default)
        => await RequestRacePageTypedAsync<JraOddsResult>(date, racecourse, raceNumber, JraPageKind.Odds, cancellationToken);

    public async Task<JraExtractionEnvelope<JraRaceResultSummary>> RequestRaceResultAsync(
        DateOnly date, string racecourse, int raceNumber, CancellationToken cancellationToken = default)
        => await RequestRacePageTypedAsync<JraRaceResultSummary>(date, racecourse, raceNumber, JraPageKind.Result, cancellationToken);

    public async Task<JraExtractionEnvelope<JraEntityProfile>> RequestHorseProfileAsync(
        string horseName, CancellationToken cancellationToken = default)
        => await RequestProfileTypedAsync(horseName, JraPageKind.HorseProfile, cancellationToken);

    /// <summary>
    /// 競走馬情報ページへクリック遷移し、プロフィールと過去の競走成績（<see cref="JraHorseScraper"/> による解析）を
    /// あわせて抽出する。
    /// </summary>
    public async Task<JraExtractionEnvelope<JraHorseProfileData>> RequestHorseProfileWithHistoryAsync(
        string horseName, CancellationToken cancellationToken = default)
    {
        var envelope = await RequestProfileAsync(
            horseName, JraPageKind.HorseProfile, ExtractHorseProfileWithHistoryAsync, cancellationToken);
        return envelope.ToTyped<JraHorseProfileData>();
    }

    public async Task<JraExtractionEnvelope<JraEntityProfile>> RequestJockeyProfileAsync(
        string jockeyName, CancellationToken cancellationToken = default)
        => await RequestProfileTypedAsync(jockeyName, JraPageKind.JockeyProfile, cancellationToken);

    public async Task<JraExtractionEnvelope<JraEntityProfile>> RequestTrainerProfileAsync(
        string trainerName, CancellationToken cancellationToken = default)
        => await RequestProfileTypedAsync(trainerName, JraPageKind.TrainerProfile, cancellationToken);

    /// <summary>
    /// JRA サイトをクリック遷移して、開催日一覧を抽出する。
    /// URL 推測・直生成は行わない。
    /// </summary>
    public async Task<JraExtractionEnvelope<JraRaceScheduleCalendar>> RequestRaceScheduleDatesAsync(
        DateOnly referenceDate,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var steps = new List<string>();
        ThrowIfDisposed();

        try
        {
            await Browser.NavigateAsync(EntryUrl, cancellationToken);
            steps.Add($"navigate: {EntryUrl}");

            var menuSnapshot = await Browser.GetPageSnapshotAsync(maxLinks: 80, cancellationToken: cancellationToken);
            var menuPage = new JraKeibaMenuParser().Parse(menuSnapshot);
            if (!menuPage.Success || menuPage.Data is null)
            {
                return JraExtractionEnvelope<JraRaceScheduleCalendar>.Failure(
                    JraPageKind.KeibaMenu,
                    Browser.CurrentUrl ?? EntryUrl,
                    new JraNavigationTrace(steps, sw.Elapsed),
                    menuPage.Error ?? "競馬メニューの解析に失敗しました。");
            }

            var scheduleLabel = menuPage.Data.ScheduleEntryText ?? "開催日程";
            await Browser.ClickAsync(scheduleLabel, cancellationToken);
            steps.Add($"click: {scheduleLabel}");
            SyncMemoryFromUrl();

            var collectedDays = new Dictionary<DateOnly, JraRaceScheduleDay>();
            var issues = new List<JraPageParseIssue>();
            var visitedMonths = new HashSet<int>();

            while (true)
            {
                var calendarSnapshot = await Browser.GetPageSnapshotAsync(maxLinks: 120, cancellationToken: cancellationToken);
                var calendarPage = new JraScheduleCalendarParser().Parse(calendarSnapshot);
                if (calendarPage.Data is null)
                {
                    break;
                }

                issues.AddRange(calendarPage.Issues);
                if (calendarPage.Data.Month is int currentMonth)
                {
                    visitedMonths.Add(currentMonth);
                }

                foreach (var day in calendarPage.Data.ScheduledDays.Where(day => day.Date >= referenceDate))
                {
                    collectedDays[day.Date] = day;
                }

                var nextMonthLink = calendarPage.Data.AvailableMonths
                    .Where(link => !visitedMonths.Contains(link.Month) && link.Month >= referenceDate.Month)
                    .OrderBy(link => link.Month)
                    .FirstOrDefault();

                if (nextMonthLink is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(nextMonthLink.Url))
                {
                    await Browser.NavigateAsync(nextMonthLink.Url, cancellationToken);
                    steps.Add($"navigate: {nextMonthLink.Url}");
                }
                else
                {
                    await Browser.ClickAsync(nextMonthLink.Text, cancellationToken);
                    steps.Add($"click: {nextMonthLink.Text}");
                }

                SyncMemoryFromUrl();
            }

            var orderedDays = collectedDays.Values.OrderBy(day => day.Date).ToList();
            var ordered = orderedDays.Select(day => day.Date).ToList();
            var sourceUrl = Browser.CurrentUrl ?? EntryUrl;

            if (ordered.Count == 0)
            {
                return JraExtractionEnvelope<JraRaceScheduleCalendar>.Failure(
                    JraPageKind.ScheduleCalendar,
                    sourceUrl,
                    new JraNavigationTrace(steps, sw.Elapsed),
                    issues.Count > 0
                        ? string.Join(" / ", issues.Select(issue => issue.Message).Distinct(StringComparer.Ordinal))
                        : "開催日程カレンダーから有効な開催日を抽出できませんでした。");
            }

            var data = new JraRaceScheduleCalendar(referenceDate, ordered, sourceUrl, orderedDays, issues);
            return new JraExtractionEnvelope<JraRaceScheduleCalendar>(
                true,
                JraPageKind.ScheduleCalendar,
                sourceUrl,
                new JraNavigationTrace(steps, sw.Elapsed),
                data,
                null);
        }
        catch (Exception ex)
        {
            return JraExtractionEnvelope<JraRaceScheduleCalendar>.Failure(
                JraPageKind.Unknown,
                _browser?.CurrentUrl ?? EntryUrl,
                new JraNavigationTrace(steps, sw.Elapsed),
                ex.Message);
        }
    }

    public async Task<JraExtractionEnvelope> ExtractCurrentPageAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        return await ExtractCurrentAsync(new List<string>(), sw, cancellationToken);
    }

    public async Task<JraStructuredPageEnvelope> ExtractCurrentStructuredPageAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var snapshot = await Browser.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: cancellationToken);
        var kind = JraPageKindDetector.Detect(Browser.CurrentUrl, snapshot);
        _memory.RecordNavigation(Browser.CurrentUrl ?? snapshot.Url, kind);
        var structured = JraStructuredPageParserRegistry.Parse(kind, snapshot);
        if (structured.Success || _registry.GetFor(kind) is not { } extractor)
        {
            return structured;
        }

        var extracted = await extractor.ExtractAsync(Browser, cancellationToken);
        return new JraStructuredPageEnvelope(
            extracted is not null,
            kind,
            Browser.CurrentUrl ?? snapshot.Url,
            extracted,
            [],
            extracted is not null ? JraPageParseConfidence.Medium : JraPageParseConfidence.Low,
            [],
            extracted is not null ? null : structured.Error);
    }

    public async Task<JraStructuredPageEnvelope> FollowStructuredNextLinkAsync(
        string relationOrLabel,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(relationOrLabel))
        {
            throw new ArgumentException("relation または label を指定してください。", nameof(relationOrLabel));
        }

        var currentPage = await ExtractCurrentStructuredPageAsync(cancellationToken);
        var nextLink = SelectRecommendedNextLink(currentPage.RecommendedNextLinks, relationOrLabel)
            ?? throw new InvalidOperationException(
                $"structured next link が見つかりませんでした: {relationOrLabel}");

        if (string.IsNullOrWhiteSpace(nextLink.Label))
        {
            throw new InvalidOperationException(
                $"structured next link にクリック可能なラベルがありませんでした: {relationOrLabel}");
        }

        await Browser.ClickAsync(nextLink.Label, cancellationToken);

        SyncMemoryFromUrl();
        return await ExtractCurrentStructuredPageAsync(cancellationToken);
    }

    public async Task NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Browser.NavigateAsync(url, cancellationToken);
        SyncMemoryFromUrl();
    }

    public async Task<PageSnapshot> GetPageSnapshotAsync(
        int maxLinks = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await Browser.GetPageSnapshotAsync(maxLinks, cancellationToken);
    }

    public async Task FollowAsync(string linkHint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Browser.ClickAsync(linkHint, cancellationToken);
        SyncMemoryFromUrl();
    }

    public async Task BackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Browser.GoBackAsync(cancellationToken);
        _memory.RecordGoBack();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }
    }

    private async Task<JraExtractionEnvelope> RequestRacePageAsync(
        DateOnly date, string racecourse, int raceNumber,
        JraPageKind targetKind, CancellationToken ct)
    {
        var sw    = Stopwatch.StartNew();
        var steps = new List<string>();
        ThrowIfDisposed();
        try
        {
            SyncMemoryFromUrl();
            await EnsureOnRacePageAsync(date, racecourse, raceNumber, steps, ct);
            await EnsurePageKindAsync(targetKind, steps, ct);

            var result = await ExtractCurrentAsync(steps, sw, ct);
            if (result.Data is not null)
            {
                return result;
            }

            steps.Add("retry: clicked race page returned no extractable data");
            await NavigateToRaceAsync(date, racecourse, raceNumber, steps, ct);
            await EnsurePageKindAsync(targetKind, steps, ct);

            var retried = await ExtractCurrentAsync(steps, sw, ct);
            if (retried.Data is not null)
            {
                return retried;
            }

            return JraExtractionEnvelope.Failure(
                targetKind,
                _browser?.CurrentUrl ?? string.Empty,
                new JraNavigationTrace(steps, sw.Elapsed),
                "クリック遷移で到達したページから有効なデータを抽出できませんでした。JRA 側の一時エラーの可能性があります。");
        }
        catch (Exception ex)
        {
            return JraExtractionEnvelope.Failure(targetKind,
                _browser?.CurrentUrl ?? string.Empty,
                new JraNavigationTrace(steps, sw.Elapsed), ex.Message);
        }
    }

    private async Task<JraExtractionEnvelope<T>> RequestRacePageTypedAsync<T>(
        DateOnly date, string racecourse, int raceNumber,
        JraPageKind targetKind, CancellationToken ct)
        where T : class
    {
        var envelope = await RequestRacePageAsync(date, racecourse, raceNumber, targetKind, ct);
        return envelope.ToTyped<T>();
    }

    private Task<JraExtractionEnvelope> RequestProfileAsync(
        string entityName, JraPageKind expectedKind, CancellationToken ct)
        => RequestProfileAsync(entityName, expectedKind, ExtractCurrentAsync, ct);

    /// <summary>
    /// エンティティ名でクリック遷移してプロフィールページへ移動し、<paramref name="extractStep"/> で
    /// 抽出処理を行う。抽出後は必ず元のページへ戻る。
    /// </summary>
    private async Task<JraExtractionEnvelope> RequestProfileAsync(
        string entityName,
        JraPageKind expectedKind,
        Func<List<string>, Stopwatch, CancellationToken, Task<JraExtractionEnvelope>> extractStep,
        CancellationToken ct)
    {
        var sw    = Stopwatch.StartNew();
        var steps = new List<string>();
        ThrowIfDisposed();

        var savedUrl  = Browser.CurrentUrl;
        var savedKind = _memory.CurrentPageKind;

        try
        {
            SyncMemoryFromUrl();

            // 出馬表ページにいることを確認する。
            // オッズ等の別ページにいる場合は保存済み URL に NavigateAsync で戻る。
            var raceCardUrl = _memory.CurrentRaceCardUrl;
            if (!string.IsNullOrWhiteSpace(raceCardUrl)
                && !string.Equals(Browser.CurrentUrl, raceCardUrl, StringComparison.Ordinal))
            {
                await Browser.NavigateAsync(raceCardUrl, ct);
                steps.Add($"navigate: {raceCardUrl}");
                SyncMemoryFromUrl();
            }

            var snapshot = await Browser.GetPageSnapshotAsync(maxLinks: 80, cancellationToken: ct);
            var primaryTarget = _planner.FindEntityLinkTarget(snapshot, entityName) ?? entityName;
            var clickCandidates = BuildEntityClickCandidates(primaryTarget);

            var clicked = false;
            foreach (var candidate in clickCandidates)
            {
                try
                {
                    await Browser.ClickAsync(candidate, ct);
                    steps.Add($"click: {candidate}");
                    SyncMemoryFromUrl();
                    clicked = true;
                    break;
                }
                catch
                {
                    // 次の候補で再試行する。
                }
            }

            if (!clicked)
                throw new InvalidOperationException($"テキスト '{entityName}' に一致するクリック可能要素が見つかりませんでした。");

            var result = await extractStep(steps, sw, ct);

            await Browser.GoBackAsync(ct);
            steps.Add("back");
            _memory.RecordNavigation(savedUrl ?? string.Empty, savedKind);

            return result;
        }
        catch (Exception ex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(savedUrl) && Browser.CurrentUrl != savedUrl)
                {
                    await Browser.GoBackAsync(ct);
                    _memory.RecordNavigation(savedUrl, savedKind);
                }
            }
            catch { /* ignore */ }

            return JraExtractionEnvelope.Failure(expectedKind,
                _browser?.CurrentUrl ?? string.Empty,
                new JraNavigationTrace(steps, sw.Elapsed), ex.Message);
        }
    }

    private async Task<JraExtractionEnvelope<JraEntityProfile>> RequestProfileTypedAsync(
        string entityName, JraPageKind expectedKind, CancellationToken ct)
    {
        var envelope = await RequestProfileAsync(entityName, expectedKind, ct);
        return envelope.ToTyped<JraEntityProfile>();
    }

    private async Task EnsureOnRacePageAsync(
        DateOnly date, string racecourse, int raceNumber,
        List<string> steps, CancellationToken ct)
    {
        if (_memory.IsCurrentRace(date, racecourse, raceNumber) && _memory.IsOnRaceRelatedPage())
            return;
        await NavigateToRaceAsync(date, racecourse, raceNumber, steps, ct);
    }

    private async Task EnsurePageKindAsync(
        JraPageKind target, List<string> steps, CancellationToken ct)
    {
        SyncMemoryFromUrl();
        if (_memory.CurrentPageKind == target) return;

        var hints = _planner.GetTransitionHints(_memory.CurrentPageKind, target)
            ?? throw new InvalidOperationException(
                $"ページ {_memory.CurrentPageKind} から {target} への直接遷移が定義されていません。");

        var currentSnapshot = await Browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: ct);
        var clickTarget = _planner.FindBestClickTarget(currentSnapshot, hints) ?? hints[0];

        await Browser.ClickAsync(clickTarget, ct);
        steps.Add($"click: {clickTarget}");
        var snapshot = await Browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: ct);
        var kind = JraPageKindDetector.Detect(Browser.CurrentUrl, snapshot);
        _memory.RecordNavigation(Browser.CurrentUrl ?? snapshot.Url, kind);

        if (_memory.CurrentPageKind != target)
        {
            throw new InvalidOperationException(
                $"クリック遷移後もページ種別が {target} になりませんでした。actual={_memory.CurrentPageKind}");
        }
    }

    private async Task NavigateToRaceAsync(
        DateOnly date, string racecourse, int raceNumber,
        List<string> steps, CancellationToken ct)
    {
        await Browser.NavigateAsync(EntryUrl, ct);
        steps.Add($"navigate: {EntryUrl}");

        await Browser.ClickAsync("出馬表", ct);
        steps.Add("click: 出馬表");

        var holdingsUrl      = Browser.CurrentUrl;
        var holdingsSnapshot = await Browser.GetPageSnapshotAsync(maxLinks: 300, cancellationToken: ct);

        if (!ContainsExactRequestedDate(holdingsSnapshot, date))
        {
            throw new InvalidOperationException(
            $"{date.Year}年{date.Month}月{date.Day}日の出馬表は現在の JRA 出馬表導線には表示されていません。別導線が必要です。");
        }

        if (await TryClickRaceLinkFromHoldingSelectionAsync(holdingsSnapshot, date, racecourse, raceNumber, steps, ct))
        {
            _memory.SetRaceContext(date, racecourse, raceNumber);
            SyncMemoryFromUrl();
            if (!string.IsNullOrWhiteSpace(Browser.CurrentUrl))
                _memory.SetRaceCardUrl(Browser.CurrentUrl);
            return;
        }

        var scopedHolding = FindHoldingLabelForDate(holdingsSnapshot, date, racecourse);
        if (!string.IsNullOrWhiteSpace(scopedHolding))
        {
            await Browser.ClickAsync(scopedHolding, ct);
            steps.Add($"click: {scopedHolding}");

            var scopedRaceListSnapshot = await Browser.GetPageSnapshotAsync(maxLinks: 120, cancellationToken: ct);
            if (ContainsExactRequestedDate(scopedRaceListSnapshot, date)
                && await TryClickRaceNumberAsync(raceNumber, steps, ct))
            {
                _memory.SetRaceContext(date, racecourse, raceNumber);
                SyncMemoryFromUrl();
                if (!string.IsNullOrWhiteSpace(Browser.CurrentUrl))
                    _memory.SetRaceCardUrl(Browser.CurrentUrl);
                return;
            }
        }

        var holdings         = ExtractHoldingLabels(holdingsSnapshot);

        if (holdings.Count == 0)
        {
            steps.Add("no holdings → assuming single race page");
            await TryClickRaceNumberAsync(raceNumber, steps, ct);
            _memory.SetRaceContext(date, racecourse, raceNumber);
            SyncMemoryFromUrl();
            if (!string.IsNullOrWhiteSpace(Browser.CurrentUrl))
                _memory.SetRaceCardUrl(Browser.CurrentUrl);
            return;
        }

        var candidates = holdings.Where(h => h.Contains(racecourse, StringComparison.Ordinal)).ToList();
        if (candidates.Count == 0) candidates = holdings.ToList();

        var dateText = $"{date.Month}月{date.Day}日";

        foreach (var holding in candidates)
        {
            if (!string.IsNullOrWhiteSpace(holdingsUrl) && Browser.CurrentUrl != holdingsUrl)
            {
                await Browser.GoBackAsync(ct);
                steps.Add("back");
                _memory.RecordGoBack();
            }

            await Browser.ClickAsync(holding, ct);
            steps.Add($"click: {holding}");

            var raceListSnapshot = await Browser.GetPageSnapshotAsync(maxLinks: 120, cancellationToken: ct);
            var hasTargetDate = ContainsExactRequestedDate(raceListSnapshot, date);

            if (!hasTargetDate) continue;

            if (await TryClickRaceNumberAsync(raceNumber, steps, ct))
            {
                _memory.SetRaceContext(date, racecourse, raceNumber);
                SyncMemoryFromUrl();
                if (!string.IsNullOrWhiteSpace(Browser.CurrentUrl))
                    _memory.SetRaceCardUrl(Browser.CurrentUrl);
                return;
            }
        }

        throw new InvalidOperationException(
            $"{date.Year}年{dateText} {racecourse}{raceNumber}R の出馬表が見つかりませんでした。");
    }

    private async Task<bool> TryClickRaceNumberAsync(
        int raceNumber, List<string> steps, CancellationToken ct)
    {
        foreach (var candidate in BuildRaceNumberClickCandidates(raceNumber))
        {
            try
            {
                await Browser.ClickAsync(candidate, ct);
                steps.Add($"click: {candidate}");
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private async Task<bool> TryClickRaceLinkFromHoldingSelectionAsync(
        PageSnapshot snapshot,
        DateOnly date,
        string racecourse,
        int raceNumber,
        List<string> steps,
        CancellationToken ct)
    {
        var directRaceLink = snapshot.Links
            .Select(link => new
            {
                link.Title,
                MatchesHeadingDate = IsLinkScopedUnderDateHeading(snapshot.MainText, link.Title, date),
                MatchesCompactDate = UrlContainsDate(link.Url, date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            })
            .Where(link => !string.IsNullOrWhiteSpace(link.Title)
                && ContainsNormalized(link.Title, racecourse)
                && ContainsNormalized(link.Title, $"{raceNumber}R"))
            .OrderByDescending(link => link.MatchesHeadingDate)
            .ThenByDescending(link => link.MatchesCompactDate)
            .FirstOrDefault(link => link.MatchesHeadingDate || link.MatchesCompactDate);

        if (directRaceLink is null)
        {
            return false;
        }

        await Browser.ClickAsync(directRaceLink.Title!, ct);
        steps.Add($"click: {directRaceLink.Title}");
        return true;
    }

    private async Task<JraExtractionEnvelope> ExtractCurrentAsync(
        List<string> steps, Stopwatch sw, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var url      = Browser.CurrentUrl ?? string.Empty;
        var snapshot = await Browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: ct);
        var kind     = JraPageKindDetector.Detect(url, snapshot);
        _memory.RecordNavigation(url, kind);

        var extractor = _registry.GetFor(kind);
        if (extractor is null)
        {
            return JraExtractionEnvelope.Failure(kind, url,
                new JraNavigationTrace(steps, sw.Elapsed),
                $"ページ種別 '{kind}' に対応する抽出器が登録されていません。");
        }

        var data = await extractor.ExtractAsync(Browser, ct);
        return new JraExtractionEnvelope(true, kind, url,
            new JraNavigationTrace(steps, sw.Elapsed), data);
    }

    private async Task<JraExtractionEnvelope> ExtractHorseProfileWithHistoryAsync(
        List<string> steps, Stopwatch sw, CancellationToken ct)
    {
        ThrowIfDisposed();
        var url = Browser.CurrentUrl ?? string.Empty;
        var data = await new JraHorseScraper(Browser).ScrapeCurrentPageAsync(ct);

        if (data is null)
        {
            return JraExtractionEnvelope.Failure(JraPageKind.HorseProfile, url,
                new JraNavigationTrace(steps, sw.Elapsed),
                "競走馬情報ページから構造化データを抽出できませんでした。");
        }

        _memory.RecordNavigation(url, JraPageKind.HorseProfile);
        return new JraExtractionEnvelope(true, JraPageKind.HorseProfile, url,
            new JraNavigationTrace(steps, sw.Elapsed), data);
    }

    private PlaywrightWebBrowser Browser
        => _browser ?? throw new ObjectDisposedException(nameof(JraSiteDataCollector));

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JraSiteDataCollector));
    }

    private void SyncMemoryFromUrl()
    {
        var url  = _browser?.CurrentUrl ?? string.Empty;
        var kind = JraPageKindDetector.Detect(url, snapshot: null);
        _memory.RecordNavigation(url, kind);
    }

    private static IReadOnlyList<string> ExtractHoldingLabels(PageSnapshot snapshot)
    {
        var sources = snapshot.Actions.Select(a => a.Text)
            .Concat(snapshot.Links.Select(l => l.Title))
            .Append(snapshot.MainText);

        return sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => HoldingLabelRegex.Matches(s!).Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string? FindHoldingLabelForDate(PageSnapshot snapshot, DateOnly date, string racecourse)
    {
        return snapshot.Links
            .Select(link => new
            {
                link.Title,
                Label = HoldingLabelRegex.Match(link.Title ?? string.Empty) is { Success: true } match ? match.Value : null,
                MatchesDate = IsLinkScopedUnderDateHeading(snapshot.MainText, link.Title ?? string.Empty, date),
            })
            .Where(link => !string.IsNullOrWhiteSpace(link.Label)
                && link.MatchesDate
                && ContainsNormalized(link.Label!, racecourse))
            .Select(link => link.Label)
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> BuildEntityClickCandidates(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return [];

        var trimmed = rawName.Trim();
        var noSpace = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal);

        return new[] { trimmed, noSpace }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
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

    private static void ExtractScheduleDates(
        PageSnapshot snapshot,
        DateOnly referenceDate,
        ISet<DateOnly> output)
    {
        static void AddIfValid(int year, int month, int day, ISet<DateOnly> target)
        {
            try
            {
                target.Add(new DateOnly(year, month, day));
            }
            catch (ArgumentOutOfRangeException)
            {
                // 不正日付は無視する。
            }
        }

        var texts = snapshot.Headings
            .Concat(snapshot.Actions.Select(a => a.Text))
            .Concat(snapshot.Links.Select(l => l.Title))
            .Append(snapshot.MainText)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        foreach (var text in texts)
        {
            var value = text!;

            foreach (Match match in FullDateRegex.Matches(value))
            {
                if (int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                    && int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                    && int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
                {
                    AddIfValid(year, month, day, output);
                }
            }

            foreach (Match match in MonthDayRegex.Matches(value))
            {
                if (int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                    && int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
                {
                    AddIfValid(referenceDate.Year, month, day, output);
                }
            }
        }
    }

    private static bool IsLinkScopedUnderDateHeading(string mainText, string linkTitle, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(mainText) || string.IsNullOrWhiteSpace(linkTitle))
        {
            return false;
        }

        var normalizedText = NormalizeLoose(mainText);
        var normalizedTitle = NormalizeLoose(linkTitle);
        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return false;
        }

        var matches = HeadingDateRegex.Matches(normalizedText);
        for (var index = 0; index < matches.Count; index++)
        {
            var heading = matches[index];
            if (!int.TryParse(heading.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                || !int.TryParse(heading.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)
                || month != date.Month
                || day != date.Day)
            {
                continue;
            }

            var start = heading.Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : normalizedText.Length;
            if (end <= start)
            {
                continue;
            }

            if (normalizedText.Substring(start, end - start).Contains(normalizedTitle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UrlContainsDate(string? url, string dateToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var match = CompactDateRegex.Match(url);
        return match.Success && string.Equals(match.Groups["date"].Value, dateToken, StringComparison.Ordinal);
    }

    private static string? ResolveUrl(string? sourceUrl, string? candidateUrl)
    {
        if (string.IsNullOrWhiteSpace(candidateUrl))
        {
            return candidateUrl;
        }

        if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var absoluteUri)
            && absoluteUri.IsAbsoluteUri
            && absoluteUri.Scheme is "http" or "https")
        {
            return absoluteUri.ToString();
        }

        if (string.IsNullOrWhiteSpace(sourceUrl)
            || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)
            || !Uri.TryCreate(sourceUri, candidateUrl, out var resolvedUri))
        {
            return candidateUrl;
        }

        return resolvedUri.ToString();
    }

    private static bool ContainsNormalized(string source, string target)
        => NormalizeLoose(source).Contains(NormalizeLoose(target), StringComparison.Ordinal);

    private static JraStructuredPageNextLink? SelectRecommendedNextLink(
        IReadOnlyList<JraStructuredPageNextLink> links,
        string relationOrLabel)
    {
        var target = NormalizeLoose(relationOrLabel);

        return links.FirstOrDefault(link => string.Equals(NormalizeLoose(link.Relation), target, StringComparison.Ordinal))
            ?? links.FirstOrDefault(link => NormalizeLoose(link.Relation).StartsWith(target + ":", StringComparison.Ordinal))
            ?? links.FirstOrDefault(link => string.Equals(NormalizeLoose(link.Label), target, StringComparison.Ordinal))
            ?? links.FirstOrDefault(link => NormalizeLoose(link.Label).Contains(target, StringComparison.Ordinal));
    }

    private static string NormalizeLoose(string value)
        => value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static bool ContainsExactRequestedDate(PageSnapshot snapshot, DateOnly date)
    {
        var fullDateText = $"{date.Year}年{date.Month}月{date.Day}日";
        var texts = snapshot.Headings
            .Concat(snapshot.Actions.Select(action => action.Text))
            .Concat(snapshot.Links.Select(link => link.Title))
            .Append(snapshot.MainText)
            .Where(text => !string.IsNullOrWhiteSpace(text));

        if (texts.Any(text => text!.Contains(fullDateText, StringComparison.Ordinal)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(snapshot.Url)
            && snapshot.Url.Contains($"calendar{date.Year}", StringComparison.Ordinal)
            && snapshot.Url.Contains($"/{date.Month}/{date.Month:D2}{date.Day:D2}.html", StringComparison.Ordinal);
    }

}

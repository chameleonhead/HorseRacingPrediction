using System.Diagnostics;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

public sealed class JraTaskAgent : IAsyncDisposable
{
    private const string EntryUrl = "https://www.jra.go.jp/keiba/";

    private static readonly Regex HoldingLabelRegex = new(
        @"\d+回(東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)\d+日",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private PlaywrightWebBrowser? _browser;
    private readonly JraSessionMemory _memory;
    private readonly JraNavigationPlanner _planner;
    private readonly JraExtractorRegistry _registry;
    private bool _disposed;

    private JraTaskAgent(PlaywrightWebBrowser browser, JraSessionMemory memory,
        JraNavigationPlanner planner, JraExtractorRegistry registry)
    {
        _browser  = browser;
        _memory   = memory;
        _planner  = planner;
        _registry = registry;
    }

    public static async Task<JraTaskAgent> CreateAsync(CancellationToken cancellationToken = default)
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
        return new JraTaskAgent(browser, memory, planner, registry);
    }

    public JraPageKind CurrentPageKind => _memory.CurrentPageKind;
    public string? CurrentUrl => _browser?.CurrentUrl;

    public async Task<JraExtractionEnvelope> RequestRaceCardAsync(
        DateOnly date, string racecourse, int raceNumber, CancellationToken cancellationToken = default)
        => await RequestRacePageAsync(date, racecourse, raceNumber, JraPageKind.RaceCard, cancellationToken);

    public async Task<JraExtractionEnvelope> RequestOddsAsync(
        DateOnly date, string racecourse, int raceNumber, CancellationToken cancellationToken = default)
        => await RequestRacePageAsync(date, racecourse, raceNumber, JraPageKind.Odds, cancellationToken);

    public async Task<JraExtractionEnvelope> RequestRaceResultAsync(
        DateOnly date, string racecourse, int raceNumber, CancellationToken cancellationToken = default)
        => await RequestRacePageAsync(date, racecourse, raceNumber, JraPageKind.Result, cancellationToken);

    public async Task<JraExtractionEnvelope> RequestHorseProfileAsync(
        string horseName, CancellationToken cancellationToken = default)
        => await RequestProfileAsync(horseName, JraPageKind.HorseProfile, cancellationToken);

    public async Task<JraExtractionEnvelope> RequestJockeyProfileAsync(
        string jockeyName, CancellationToken cancellationToken = default)
        => await RequestProfileAsync(jockeyName, JraPageKind.JockeyProfile, cancellationToken);

    public async Task<JraExtractionEnvelope> RequestTrainerProfileAsync(
        string trainerName, CancellationToken cancellationToken = default)
        => await RequestProfileAsync(trainerName, JraPageKind.TrainerProfile, cancellationToken);

    public async Task<JraExtractionEnvelope> ExtractCurrentPageAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        return await ExtractCurrentAsync(new List<string>(), sw, cancellationToken);
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
            return await ExtractCurrentAsync(steps, sw, ct);
        }
        catch (Exception ex)
        {
            return JraExtractionEnvelope.Failure(targetKind,
                _browser?.CurrentUrl ?? string.Empty,
                new JraNavigationTrace(steps, sw.Elapsed), ex.Message);
        }
    }

    private async Task<JraExtractionEnvelope> RequestProfileAsync(
        string entityName, JraPageKind expectedKind, CancellationToken ct)
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

            var result = await ExtractCurrentAsync(steps, sw, ct);

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

        var directUrl = TryBuildDirectRacePageUrl(Browser.CurrentUrl, target);
        if (!string.IsNullOrWhiteSpace(directUrl))
        {
            await Browser.NavigateAsync(directUrl, ct);
            steps.Add($"navigate: {directUrl}");
            SyncMemoryFromUrl();
            if (_memory.CurrentPageKind == target) return;
        }

        var hints = _planner.GetTransitionHints(_memory.CurrentPageKind, target)
            ?? throw new InvalidOperationException(
                $"ページ {_memory.CurrentPageKind} から {target} への直接遷移が定義されていません。");

        var snapshot    = await Browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: ct);
        var clickTarget = _planner.FindBestClickTarget(snapshot, hints) ?? hints[0];

        await Browser.ClickAsync(clickTarget, ct);
        steps.Add($"click: {clickTarget}");
        SyncMemoryFromUrl();

        if (_memory.CurrentPageKind != target)
        {
            directUrl = TryBuildDirectRacePageUrl(Browser.CurrentUrl, target);
            if (!string.IsNullOrWhiteSpace(directUrl))
            {
                await Browser.NavigateAsync(directUrl, ct);
                steps.Add($"navigate: {directUrl}");
                SyncMemoryFromUrl();
            }
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
        var holdingsSnapshot = await Browser.GetPageSnapshotAsync(maxLinks: 30, cancellationToken: ct);
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
                await Browser.NavigateAsync(holdingsUrl, ct);

            await Browser.ClickAsync(holding, ct);
            steps.Add($"click: {holding}");

            var raceListSnapshot = await Browser.GetPageSnapshotAsync(maxLinks: 0, cancellationToken: ct);
            var hasTargetDate =
                raceListSnapshot.MainText.Contains(dateText, StringComparison.Ordinal)
                || raceListSnapshot.Headings.Any(h => h.Contains(dateText, StringComparison.Ordinal));

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
        try
        {
            await Browser.ClickAsync($"{raceNumber}レース", ct);
            steps.Add($"click: {raceNumber}レース");
            return true;
        }
        catch { return false; }
    }

    private async Task<JraExtractionEnvelope> ExtractCurrentAsync(
        List<string> steps, Stopwatch sw, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var url      = Browser.CurrentUrl ?? string.Empty;
        var snapshot = await Browser.GetPageSnapshotAsync(maxLinks: 0, cancellationToken: ct);
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

    private PlaywrightWebBrowser Browser
        => _browser ?? throw new ObjectDisposedException(nameof(JraTaskAgent));

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JraTaskAgent));
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

    private static string? TryBuildDirectRacePageUrl(string? currentUrl, JraPageKind target)
    {
        if (string.IsNullOrWhiteSpace(currentUrl)) return null;

        var current = currentUrl;
        var converted = target switch
        {
            JraPageKind.RaceCard => current
                .Replace("accessO.html", "accessD.html", StringComparison.OrdinalIgnoreCase)
                .Replace("accessP.html", "accessD.html", StringComparison.OrdinalIgnoreCase),
            JraPageKind.Odds => current
                .Replace("accessD.html", "accessO.html", StringComparison.OrdinalIgnoreCase)
                .Replace("accessP.html", "accessO.html", StringComparison.OrdinalIgnoreCase),
            JraPageKind.Result => current
                .Replace("accessD.html", "accessP.html", StringComparison.OrdinalIgnoreCase)
                .Replace("accessO.html", "accessP.html", StringComparison.OrdinalIgnoreCase),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(converted)) return null;
        if (string.Equals(converted, current, StringComparison.Ordinal)) return null;
        return converted;
    }
}

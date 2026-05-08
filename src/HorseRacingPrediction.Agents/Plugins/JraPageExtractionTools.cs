using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// JRA サイト専用のページ抽出クライアント。
/// このクライアントは自身で Playwright セッションを保持し、
/// 現在ページから最短で必要情報に到達するための API を提供する。
/// </summary>
public sealed class JraPageExtractionTools : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] OddsKeywords =
    [
        "オッズ",
        "単勝",
        "複勝",
        "馬連",
        "馬単",
        "ワイド",
        "三連複",
        "三連単"
    ];

    private static readonly string[] RaceCardKeywords = ["出馬表"];

    private readonly SemaphoreSlim _lock = new(1, 1);
    private PlaywrightWebBrowser? _browser;
    private JraRaceCardScraper? _raceCardScraper;

    [Description("JRA専用セッションを開始してURLを開きます。以後の操作は同じPlaywrightセッションで継続されます。")]
    public async Task<string> OpenJraPage(
        [Description("開くURL。例: https://www.jra.go.jp/keiba/thisweek/")] string url,
        CancellationToken cancellationToken = default)
    {
        return await WithSessionAsync(async browser =>
        {
            await browser.NavigateAsync(url, cancellationToken);
            var snapshot = await browser.GetPageSnapshotAsync(maxLinks: 30, cancellationToken: cancellationToken);
            return Serialize(new
            {
                status = "ok",
                action = "open",
                currentUrl = browser.CurrentUrl,
                page = BuildPageSummary(snapshot)
            });
        });
    }

    [Description("現在ページから最短導線で指定ターゲット情報を抽出します。target=odds ならオッズ画面へ、target=race_card なら出馬表へ優先遷移して抽出します。")]
    public async Task<string> ExtractFromCurrentPage(
        [Description("抽出ターゲット。odds / race_card / snapshot")] string target = "snapshot",
        [Description("最短遷移で許可する最大クリック回数。既定2")] int maxSteps = 2,
        CancellationToken cancellationToken = default)
    {
        return await WithSessionAsync(async browser =>
        {
            if (string.IsNullOrWhiteSpace(browser.CurrentUrl))
            {
                return "セッション内でページが開かれていません。最初に OpenJraPage を呼び出してください。";
            }

            var normalizedTarget = target.Trim().ToLowerInvariant();
            var route = new List<string>();
            var steps = Math.Max(0, maxSteps);

            switch (normalizedTarget)
            {
                case "odds":
                    await NavigateByKeywordsAsync(browser, OddsKeywords, steps, route, cancellationToken);
                    break;
                case "race_card":
                    await NavigateByKeywordsAsync(browser, RaceCardKeywords, steps, route, cancellationToken);
                    break;
                case "snapshot":
                    break;
                default:
                    return $"未対応 target: {target}. target は odds / race_card / snapshot を指定してください。";
            }

            var snapshot = await browser.GetPageSnapshotAsync(maxLinks: 40, cancellationToken: cancellationToken);

            object? structured = null;
            if (normalizedTarget == "race_card")
            {
                _raceCardScraper ??= new JraRaceCardScraper(browser);
                structured = await _raceCardScraper.ScrapeCurrentPageAsync(cancellationToken);
            }

            var result = new
            {
                status = "ok",
                target = normalizedTarget,
                currentUrl = browser.CurrentUrl,
                route,
                page = BuildPageSummary(snapshot),
                structured
            };

            return Serialize(result);
        });
    }

    [Description("現在ページからオッズ画面への最短遷移を実行し、到達先ページの要約を返します。")]
    public async Task<string> NavigateToOddsFromCurrentPage(
        [Description("許可する最大クリック回数。既定2")] int maxSteps = 2,
        CancellationToken cancellationToken = default)
    {
        return await ExtractFromCurrentPage("odds", maxSteps, cancellationToken);
    }

    [Description("現在ページのスナップショット要約を取得します。遷移は行いません。")]
    public async Task<string> GetCurrentPageSnapshot(CancellationToken cancellationToken = default)
    {
        return await ExtractFromCurrentPage("snapshot", 0, cancellationToken);
    }

    [Description("JRA専用Playwrightセッションを終了します。次回呼び出し時に新しいセッションが開始されます。")]
    public async Task<string> CloseJraSession()
    {
        await _lock.WaitAsync();
        try
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
                _browser = null;
                _raceCardScraper = null;
            }
        }
        finally
        {
            _lock.Release();
        }

        return Serialize(new { status = "ok", action = "close_session" });
    }

    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(OpenJraPage),
        AIFunctionFactory.Create(ExtractFromCurrentPage),
        AIFunctionFactory.Create(NavigateToOddsFromCurrentPage),
        AIFunctionFactory.Create(GetCurrentPageSnapshot),
        AIFunctionFactory.Create(CloseJraSession)
    ];

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
                _browser = null;
                _raceCardScraper = null;
            }
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }

    private async Task<string> WithSessionAsync(Func<PlaywrightWebBrowser, Task<string>> action)
    {
        await _lock.WaitAsync();
        try
        {
            _browser ??= await PlaywrightWebBrowser.CreateAsync();
            return await action(_browser);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task NavigateByKeywordsAsync(
        IWebBrowser browser,
        IReadOnlyList<string> keywords,
        int maxSteps,
        List<string> route,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            var snapshot = await browser.GetPageSnapshotAsync(maxLinks: 60, cancellationToken: cancellationToken);
            var candidate = SelectBestClickableText(snapshot, keywords);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            await browser.ClickAsync(candidate, cancellationToken);
            route.Add(candidate);
        }
    }

    private static string? SelectBestClickableText(PageSnapshot snapshot, IReadOnlyList<string> keywords)
    {
        var actionTexts = snapshot.Actions
            .Select(a => a.Text?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        foreach (var keyword in keywords)
        {
            var fromAction = actionTexts.FirstOrDefault(t => t!.Contains(keyword, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(fromAction)) return fromAction;

            var fromLink = snapshot.Links
                .Select(l => l.Title?.Trim())
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && t.Contains(keyword, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(fromLink)) return fromLink;
        }

        return null;
    }

    private static object BuildPageSummary(PageSnapshot snapshot)
    {
        var text = snapshot.MainText ?? string.Empty;
        var mainText = text.Length <= 1200 ? text : text[..1200];

        return new
        {
            url = snapshot.Url,
            title = snapshot.Title,
            headings = snapshot.Headings.Take(20).ToArray(),
            actions = snapshot.Actions.Select(a => a.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal).Take(30).ToArray(),
            links = snapshot.Links.Select(l => new { l.Title, l.Url }).Take(30).ToArray(),
            tableCount = snapshot.Tables.Count,
            tableHeaders = snapshot.Tables.Take(5).Select(t => t.Headers).ToArray(),
            mainText
        };
    }

    private static string Serialize(object value)
        => JsonSerializer.Serialize(value, JsonOptions);
}
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// Chrome 相当のブラウザ操作を単一ページ単位で完結させるプラグイン。
/// 検索はアドレスバー相当の操作で行い、ページ取得は 1 回のツール呼び出し内で完了する。
/// 詳細表示が必要な場合のみ、同じ呼び出し内で 1 回だけ追加クリックを行う。
/// <see cref="WebBrowserAgent"/> のツールとして使用することを想定している。
/// </summary>
public sealed class PlaywrightTools
{
    private static readonly ConditionalWeakTable<IWebBrowser, SemaphoreSlim> BrowserLocks = new();
    private const int MaxLoggedTextLength = 8_000;

    private readonly IWebBrowser _browser;
    private readonly WebFetchOptions _options;
    private readonly PageDataExtractionAgent? _extractionAgent;
    private readonly ILogger<PlaywrightTools> _logger;

    public PlaywrightTools(
        IWebBrowser browser,
        IOptions<WebFetchOptions> options,
        PageDataExtractionAgent? extractionAgent = null,
        ILogger<PlaywrightTools>? logger = null)
    {
        _browser = browser;
        _options = options.Value;
        _extractionAgent = extractionAgent;
        _logger = logger ?? NullLogger<PlaywrightTools>.Instance;
    }

    /// <summary>
    /// Chrome のアドレスバー相当の検索を行い、検索結果ページの DOM からリンク一覧を抽出する。
    /// </summary>
    [Description("Chrome のアドレスバー相当の検索でクエリを実行し、検索結果ページの DOM から抽出したリンク一覧と結果ページテキストを返します。リンク名は DOM の表示名をそのまま保持します。")]
    public async Task<string> BrowserSearch(
        [Description("検索クエリ（スペース区切りのキーワード）")] string query,
        [Description("検索対象サイトのドメイン（例: www.jra.go.jp）省略可")] string? site = null,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = string.IsNullOrWhiteSpace(site)
            ? query
            : $"{query} site:{site}";

        return await UseBrowserAsync(async ct =>
        {
            _logger.LogInformation("PlaywrightTools BrowserSearch start. Query={Query} Site={Site}", query, site);
            var rawText = await _browser.SearchAsync(searchQuery, ct);
            var currentUrl = NormalizePageUrl(_browser.CurrentUrl) ?? string.Empty;
            var snapshot = await _browser.GetPageSnapshotAsync(GetSearchLinkLimit(), ct);
            var links = NormalizeLinks(await _browser.GetLinksAsync(GetSearchLinkLimit(), ct), currentUrl);

            if (snapshot.Links.Count > 0)
            {
                links = NormalizeLinks(snapshot.Links, currentUrl);
                snapshot = snapshot with { Links = links };
            }

            if (string.IsNullOrWhiteSpace(rawText) && links.Count == 0)
            {
                return "検索結果が見つかりませんでした。";
            }

            var formatted = await FormatIfAvailableAsync(rawText, currentUrl, links, snapshot, ct);
            _logger.LogInformation(
                "PlaywrightTools BrowserSearch complete. Query={Query} CurrentUrl={CurrentUrl} LinkCount={LinkCount} RawTextLength={RawTextLength} FormattedLength={FormattedLength}",
                searchQuery,
                currentUrl,
                links.Count,
                rawText.Length,
                formatted.Length);
            return BuildSearchResult(searchQuery, currentUrl, links, formatted);
        }, cancellationToken);
    }

    /// <summary>
    /// 指定した URL の単一ページを取得し、必要時のみ同一呼び出し内で 1 回だけ詳細表示をクリックして本文を返す。
    /// </summary>
    [Description("指定した URL のページを読み取り、不要部分を除去した本文を返します。現在ページだけでは詳細に到達できないと LLM が判断した場合に限り、同一呼び出し内で 1 回だけ詳細表示をクリックします。")]
    public async Task<string> BrowserReadPage(
        [Description("取得対象の URL")] string url,
        [Description("取得目的。詳細表示が必要かの判断補助に使います。省略可")] string? objective = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDomain(url);

        return await UseBrowserAsync(async ct =>
        {
            _logger.LogInformation("PlaywrightTools BrowserReadPage start. Url={Url} Objective={Objective}", url, objective);
            var rawText = await _browser.NavigateAsync(url, ct);
            var currentUrl = NormalizePageUrl(_browser.CurrentUrl) ?? NormalizePageUrl(url) ?? url;
            var snapshot = await _browser.GetPageSnapshotAsync(GetPageLinkLimit(), ct);
            var links = NormalizeLinks(await _browser.GetLinksAsync(GetPageLinkLimit(), ct), currentUrl);

            if (snapshot.Links.Count > 0)
            {
                links = NormalizeLinks(snapshot.Links, currentUrl);
                snapshot = snapshot with { Url = currentUrl, Links = links };
            }

            var extraction = await AnalyzePageAsync(rawText, currentUrl, objective, links, snapshot, ct);
            if (extraction.ShouldFollowDetailLink && !string.IsNullOrWhiteSpace(extraction.DetailLinkText))
            {
                var safeDetailClickText = ResolveSafeDetailClickText(extraction.DetailLinkText, links, snapshot);
                if (safeDetailClickText is null)
                {
                    _logger.LogWarning(
                        "PlaywrightTools BrowserReadPage skipped detail click because the target was ambiguous or not found. CurrentUrl={CurrentUrl} DetailLinkText={DetailLinkText}",
                        currentUrl,
                        extraction.DetailLinkText);
                }
                else
                {
                try
                {
                    _logger.LogInformation(
                        "PlaywrightTools BrowserReadPage follow detail link. CurrentUrl={CurrentUrl} DetailLinkText={DetailLinkText}",
                        currentUrl,
                        safeDetailClickText);
                    rawText = await _browser.ClickAsync(safeDetailClickText, ct);
                    currentUrl = NormalizePageUrl(_browser.CurrentUrl) ?? currentUrl;

                    if (!IsDomainAllowed(currentUrl))
                    {
                        try { await _browser.GoBackAsync(ct); } catch { }
                    }
                    else
                    {
                        snapshot = await _browser.GetPageSnapshotAsync(GetPageLinkLimit(), ct);
                        links = NormalizeLinks(await _browser.GetLinksAsync(GetPageLinkLimit(), ct), currentUrl);
                        if (snapshot.Links.Count > 0)
                        {
                            links = NormalizeLinks(snapshot.Links, currentUrl);
                            snapshot = snapshot with { Url = currentUrl, Links = links };
                        }

                        extraction = await AnalyzePageAsync(rawText, currentUrl, objective, links, snapshot, ct);
                    }
                }
                catch (InvalidOperationException)
                {
                    // 詳細クリックが失敗した場合は初回取得結果をそのまま使う。
                }
                }
            }

            _logger.LogInformation(
                "PlaywrightTools BrowserReadPage complete. Url={Url} CurrentUrl={CurrentUrl} LinkCount={LinkCount} ContentLength={ContentLength}",
                url,
                currentUrl,
                links.Count,
                extraction.ContentMarkdown.Length);
            return BuildPageResult(currentUrl, extraction.ContentMarkdown, links);
        }, cancellationToken);
    }

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(BrowserSearch),
        AIFunctionFactory.Create(BrowserReadPage),
    ];

    // ------------------------------------------------------------------ //
    // helpers
    // ------------------------------------------------------------------ //

    private async Task<string> FormatIfAvailableAsync(
        string rawText,
        string url,
        IReadOnlyList<SearchResultLink> pageLinks,
        PageSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (_extractionAgent is null)
            return rawText;

        if (snapshot is not null)
        {
            LogAiInput("FormatPageContent", url, null, rawText, pageLinks, snapshot);
            var formatted = await _extractionAgent.FormatPageContentAsync(snapshot, cancellationToken);
            LogAiOutput("FormatPageContent", url, formatted);
            return formatted;
        }

        LogAiInput("FormatPageContent", url, null, rawText, pageLinks, null);
        var rawFormatted = await _extractionAgent.FormatPageContentAsync(rawText, url, pageLinks, cancellationToken);
        LogAiOutput("FormatPageContent", url, rawFormatted);
        return rawFormatted;
    }

    private static string? NormalizePageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri) &&
               absoluteUri.Scheme is "http" or "https"
            ? absoluteUri.AbsoluteUri
            : null;
    }

    private static IReadOnlyList<SearchResultLink> NormalizeLinks(
        IReadOnlyList<SearchResultLink> links,
        string? baseUrl)
    {
        if (links.Count == 0)
        {
            return links;
        }

        Uri? baseUri = null;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            Uri.TryCreate(baseUrl, UriKind.Absolute, out baseUri);
        }

        var normalized = new List<SearchResultLink>(links.Count);
        foreach (var link in links)
        {
            var normalizedUrl = NormalizeLinkUrl(link.Url, baseUri);
            if (normalizedUrl is null)
            {
                continue;
            }

            normalized.Add(link with { Url = normalizedUrl });
        }

        return normalized;
    }

    private static string? NormalizeLinkUrl(string? url, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri) &&
            absoluteUri.Scheme is "http" or "https")
        {
            return absoluteUri.AbsoluteUri;
        }

        if (baseUri is not null && Uri.TryCreate(baseUri, url, out var resolvedUri) &&
            resolvedUri.Scheme is "http" or "https")
        {
            return resolvedUri.AbsoluteUri;
        }

        return null;
    }

    private async Task<PageExtractionResult> AnalyzePageAsync(
        string rawText,
        string url,
        string? objective,
        IReadOnlyList<SearchResultLink> links,
        PageSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (_extractionAgent is null)
        {
            return new PageExtractionResult(rawText, false, null);
        }

        if (snapshot is not null)
        {
            LogAiInput("AnalyzePage", url, objective, rawText, links, snapshot);
            var extraction = await _extractionAgent.AnalyzePageAsync(snapshot, objective, cancellationToken);
            LogAiOutput("AnalyzePage", url, extraction);
            return extraction;
        }

        LogAiInput("AnalyzePage", url, objective, rawText, links, null);
        var rawExtraction = await _extractionAgent.AnalyzePageAsync(rawText, url, objective, links, cancellationToken);
        LogAiOutput("AnalyzePage", url, rawExtraction);
        return rawExtraction;
    }

    private async Task<string> UseBrowserAsync(
        Func<CancellationToken, Task<string>> action,
        CancellationToken cancellationToken)
    {
        var browserLock = BrowserLocks.GetValue(_browser, _ => new SemaphoreSlim(1, 1));
        await browserLock.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            browserLock.Release();
        }
    }

    private string BuildSearchResult(
        string searchQuery,
        string currentUrl,
        IReadOnlyList<SearchResultLink> links,
        string formattedText)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"検索: {searchQuery}");
        if (!string.IsNullOrWhiteSpace(currentUrl))
        {
            sb.AppendLine($"[現在のページ: {currentUrl}]");
        }

        sb.AppendLine();
        sb.AppendLine("## 検索結果リンク");
    AppendLinks(sb, links, "検索結果リンクが見つかりませんでした。", groupByRegion: false);

        if (!string.IsNullOrWhiteSpace(formattedText))
        {
            sb.AppendLine();
            sb.AppendLine("## 検索結果ページ");
            sb.AppendLine(formattedText);
        }

        return sb.ToString().TrimEnd();
    }

    private string BuildPageResult(
        string currentUrl,
        string contentMarkdown,
        IReadOnlyList<SearchResultLink> links)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(currentUrl))
        {
            sb.AppendLine($"[現在のページ: {currentUrl}]");
            sb.AppendLine();
        }

        sb.AppendLine(contentMarkdown);
        sb.AppendLine();
        AppendGroupedLinks(sb, links);
        return sb.ToString().TrimEnd();
    }

    private static void AppendLinks(
        StringBuilder sb,
        IReadOnlyList<SearchResultLink> links,
        string emptyMessage,
        bool groupByRegion)
    {
        if (links.Count == 0)
        {
            sb.AppendLine(emptyMessage);
            return;
        }

        if (groupByRegion)
        {
            AppendGroupedLinks(sb, links);
            return;
        }

        foreach (var link in links)
        {
            var title = string.IsNullOrWhiteSpace(link.Title) ? link.Url : link.Title;
            sb.AppendLine($"- [{title}]({link.Url})");
        }
    }

    private static void AppendGroupedLinks(StringBuilder sb, IReadOnlyList<SearchResultLink> links)
    {
        if (links.Count == 0)
        {
            sb.AppendLine("## リンク");
            sb.AppendLine("リンクが見つかりませんでした。");
            return;
        }

        AppendLinkSection(sb, "## ヘッダーリンク", links.Where(link => link.Region == "header").ToList());
        AppendLinkSection(sb, "## フッターリンク", links.Where(link => link.Region == "footer").ToList());
        AppendLinkSection(sb, "## リンク", links.Where(link => link.Region != "header" && link.Region != "footer").ToList());
    }

    private static void AppendLinkSection(
        StringBuilder sb,
        string heading,
        IReadOnlyList<SearchResultLink> links)
    {
        if (links.Count == 0)
        {
            return;
        }

        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.AppendLine();
        }

        sb.AppendLine(heading);
        foreach (var link in links)
        {
            var title = string.IsNullOrWhiteSpace(link.Title) ? link.Url : link.Title;
            sb.AppendLine($"- [{title}]({link.Url})");
        }
        sb.AppendLine();
    }

    private int GetSearchLinkLimit()
    {
        return Math.Max(1, _options.MaxSearchLinksPerPage);
    }

    private int GetPageLinkLimit()
    {
        return Math.Max(1, _options.MaxLinksPerPage);
    }

    private static string? ResolveSafeDetailClickText(
        string detailLinkText,
        IReadOnlyList<SearchResultLink> links,
        PageSnapshot? snapshot)
    {
        var candidate = NormalizeMatchText(detailLinkText);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var linkMatches = links
            .Where(link => string.Equals(NormalizeMatchText(link.Title), candidate, StringComparison.Ordinal))
            .Select(link => link.Title)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var actionMatches = snapshot?.Actions
            .Where(action => string.Equals(NormalizeMatchText(action.Text), candidate, StringComparison.Ordinal))
            .Select(action => action.Text)
            .Distinct(StringComparer.Ordinal)
            .ToList()
            ?? [];

        var matchCount = linkMatches.Count + actionMatches.Count;
        if (matchCount != 1)
        {
            return null;
        }

        return linkMatches.Count == 1 ? linkMatches[0] : actionMatches[0];
    }

    private static string NormalizeMatchText(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private void LogAiInput(
        string operation,
        string url,
        string? objective,
        string rawText,
        IReadOnlyList<SearchResultLink> links,
        PageSnapshot? snapshot)
    {
        _logger.LogInformation(
            "PlaywrightTools AI IN Operation={Operation} Url={Url} Objective={Objective} RawTextLength={RawTextLength} LinkCount={LinkCount} Snapshot={Snapshot}\nRawText:\n{RawText}\nLinks:\n{Links}\nSnapshotJson:\n{SnapshotJson}",
            operation,
            url,
            objective ?? string.Empty,
            rawText.Length,
            links.Count,
            snapshot is null ? "none" : "present",
            ClipForLog(rawText),
            ClipForLog(BuildLinksLogText(links)),
            ClipForLog(snapshot is null ? string.Empty : BuildSnapshotLogText(snapshot)));
    }

    private void LogAiOutput(string operation, string url, string output)
    {
        _logger.LogInformation(
            "PlaywrightTools AI OUT Operation={Operation} Url={Url}\nOutput:\n{Output}",
            operation,
            url,
            ClipForLog(output));
    }

    private void LogAiOutput(string operation, string url, PageExtractionResult output)
    {
        _logger.LogInformation(
            "PlaywrightTools AI OUT Operation={Operation} Url={Url} ShouldFollowDetailLink={ShouldFollowDetailLink} DetailLinkText={DetailLinkText}\nContentMarkdown:\n{ContentMarkdown}",
            operation,
            url,
            output.ShouldFollowDetailLink,
            output.DetailLinkText,
            ClipForLog(output.ContentMarkdown));
    }

    private static string BuildLinksLogText(IReadOnlyList<SearchResultLink> links)
    {
        if (links.Count == 0)
        {
            return "(リンクなし)";
        }

        return string.Join(Environment.NewLine, links.Select(link => $"- {link.Title} | {link.Url} | {link.Region}"));
    }

    private static string BuildSnapshotLogText(PageSnapshot snapshot)
    {
        var compactSnapshot = new
        {
            snapshot.Url,
            snapshot.Title,
            snapshot.Headings,
            MainText = snapshot.MainText.Length <= 4_000 ? snapshot.MainText : snapshot.MainText[..4_000],
            Links = snapshot.Links.Take(50).Select(link => new { link.Title, link.Url, link.Region }),
            Actions = snapshot.Actions.Take(30).Select(action => new { action.Text, action.Kind }),
            Tables = snapshot.Tables.Take(5).Select(table => new
            {
                Headers = table.Headers,
                Rows = table.Rows.Take(10)
            })
        };

        return System.Text.Json.JsonSerializer.Serialize(compactSnapshot, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static string ClipForLog(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= MaxLoggedTextLength
            ? text
            : $"{text[..MaxLoggedTextLength]}{Environment.NewLine}...(truncated)";
    }

    private void ValidateDomain(string url)
    {
        if (_options.AllowedDomains.Count == 0)
        {
            throw new InvalidOperationException(
                "アクセスが許可されているドメインがありません。" +
                "appsettings.json の WebFetch:AllowedDomains を設定してください。");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"URL の形式が不正です: {url}", nameof(url));
        }

        ValidateHost(uri.Host);
    }

    private bool IsDomainAllowed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return true; // 相対 URL やスキーム不明は許可
        }

        var lower = uri.Host.ToLowerInvariant();
        return _options.AllowedDomains
            .Any(d => lower == d.ToLowerInvariant() || lower.EndsWith("." + d.ToLowerInvariant()));
    }

    private void ValidateHost(string host)
    {
        var lower = host.ToLowerInvariant();
        var allowed = _options.AllowedDomains
            .Any(d => lower == d.ToLowerInvariant() || lower.EndsWith("." + d.ToLowerInvariant()));

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"ドメイン '{host}' へのアクセスは許可されていません。" +
                "appsettings.json の WebFetch:AllowedDomains に追加してください。");
        }
    }
}

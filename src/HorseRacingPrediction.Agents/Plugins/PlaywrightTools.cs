using System.ComponentModel;
using System.Text;
using HorseRacingPrediction.Agents.Browser;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// Playwright ベースのブラウザ操作プリミティブを提供するプラグイン。
/// Microsoft Playwright MCP のインターフェースを参考に、
/// ページ移動・テキスト取得・リンク抽出・検索エンジン利用の各操作を公開する。
/// <see cref="WebBrowserAgent"/> のツールとして使用することを想定している。
/// </summary>
public sealed class PlaywrightTools
{
    private readonly IWebBrowser _browser;
    private readonly WebFetchOptions _options;

    public PlaywrightTools(IWebBrowser browser, IOptions<WebFetchOptions> options)
    {
        _browser = browser;
        _options = options.Value;
    }

    /// <summary>
    /// 指定した URL に移動してページの本文テキストを取得する。
    /// Playwright MCP の <c>browser_navigate</c> に相当する。
    /// </summary>
    [Description("指定した URL に移動してページの本文テキストを取得します。")]
    public async Task<string> BrowserNavigate(
        [Description("移動先の URL")] string url,
        CancellationToken cancellationToken = default)
    {
        ValidateDomain(url);
        return await _browser.FetchTextAsync(url, cancellationToken);
    }

    /// <summary>
    /// 指定した URL のページ内リンク一覧を抽出する。
    /// 検索結果ページや一般ページの両方に対応し、リンクを Markdown リスト形式で返す。
    /// Playwright MCP の <c>browser_snapshot</c> からリンク情報を抽出する操作に相当する。
    /// </summary>
    [Description("指定した URL のページ内リンク一覧を抽出します。検索結果ページや一般ページに対して使用します。")]
    public async Task<string> BrowserGetLinks(
        [Description("リンクを抽出するページの URL")] string url,
        [Description("抽出する最大リンク数（既定値 10）")] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        ValidateDomain(url);
        var links = await _browser.ExtractLinksAsync(url, maxResults, cancellationToken);

        if (links.Count == 0)
            return "リンクが見つかりませんでした。";

        var sb = new StringBuilder();
        foreach (var link in links)
            sb.AppendLine($"- [{link.Title}]({link.Url})");
        return sb.ToString();
    }

    /// <summary>
    /// 検索エンジンでクエリを実行し、検索結果のリンク一覧を取得する。
    /// site パラメータでドメイン絞り込みが可能。
    /// </summary>
    [Description("検索エンジンでクエリを実行し、検索結果のリンク一覧を取得します。")]
    public async Task<string> BrowserSearch(
        [Description("検索クエリ文字列")] string query,
        [Description("検索対象を絞り込むサイトドメイン（省略可）")] string? site = null,
        [Description("取得する最大リンク数（既定値 10）")] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = string.IsNullOrWhiteSpace(site)
            ? query
            : $"{query} site:{site}";

        var links = await _browser.SearchAsync(searchQuery, maxResults, cancellationToken);

        if (links.Count == 0)
        {
            return "検索結果リンクを取得できませんでした。";
        }

        var sb = new StringBuilder();
        foreach (var link in links)
            sb.AppendLine($"- [{link.Title}]({link.Url})");
        return sb.ToString();
    }

    /// <summary>
    /// 検索エンジンでクエリを実行し、上位ページに実際にアクセスして本文テキストを取得する。
    /// 検索 → ページ読み込みを 1 回のツール呼び出しで行う複合操作。
    /// 小規模モデルで検索後にページ読み込みを忘れる問題を防ぐ。
    /// </summary>
    [Description("検索して上位ページの本文テキストを一括取得します。検索結果のリンク先を実際に開いて内容を読みます。")]
    public async Task<string> BrowserSearchAndRead(
        [Description("検索クエリ文字列")] string query,
        [Description("検索対象を絞り込むサイトドメイン（省略可）")] string? site = null,
        [Description("読み込むページ数（既定値 3）")] int maxPages = 3,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = string.IsNullOrWhiteSpace(site)
            ? query
            : $"{query} site:{site}";

        var links = await _browser.SearchAsync(searchQuery, maxPages * 3, cancellationToken);

        if (links.Count == 0)
        {
            return "検索結果が見つかりませんでした。";
        }

        var sb = new StringBuilder();
        var fetched = 0;
        foreach (var link in links)
        {
            if (fetched >= maxPages) break;
            try
            {
                var content = await _browser.FetchTextAsync(link.Url, cancellationToken);
                sb.AppendLine($"### {link.Title}");
                sb.AppendLine($"URL: {link.Url}");
                sb.AppendLine();
                sb.AppendLine(TrimContent(content));
                sb.AppendLine();
                fetched++;
            }
            catch
            {
                // skip inaccessible pages
            }
        }

        if (fetched == 0)
        {
            return "検索結果ページを読み込めませんでした。";
        }

        return sb.ToString();
    }

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(BrowserNavigate),
        AIFunctionFactory.Create(BrowserGetLinks),
        AIFunctionFactory.Create(BrowserSearch),
        AIFunctionFactory.Create(BrowserSearchAndRead),
    ];

    // ------------------------------------------------------------------ //
    // helpers
    // ------------------------------------------------------------------ //

    private static string TrimContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        const int maxLength = 8_000;
        return content.Length <= maxLength
            ? content
            : content[..maxLength] + "\n\n（以下省略）";
    }

    // ------------------------------------------------------------------ //
    // domain validation
    // ------------------------------------------------------------------ //

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

        var host = uri.Host.ToLowerInvariant();
        var allowed = _options.AllowedDomains
            .Any(d => host == d.ToLowerInvariant() || host.EndsWith("." + d.ToLowerInvariant()));

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"ドメイン '{host}' へのアクセスは許可されていません。" +
                "appsettings.json の WebFetch:AllowedDomains に追加してください。");
        }
    }
}

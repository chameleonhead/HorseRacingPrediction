using System.ComponentModel;
using System.Text;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// セッションベースの Playwright ブラウザ操作ツールを提供するプラグイン。
/// ブラウザのページはセッション中ずっと開いたままで、エージェントが
/// ナビゲーション・クリック・テキスト取得・リンク抽出・検索・戻るなどの
/// 操作を逐次実行してインタラクティブに Web ページを閲覧する。
/// <see cref="WebBrowserAgent"/> のツールとして使用することを想定している。
/// </summary>
public sealed class PlaywrightTools
{
    private readonly IWebBrowser _browser;
    private readonly WebFetchOptions _options;
    private readonly PageDataExtractionAgent? _extractionAgent;

    public PlaywrightTools(IWebBrowser browser, IOptions<WebFetchOptions> options, PageDataExtractionAgent? extractionAgent = null)
    {
        _browser = browser;
        _options = options.Value;
        _extractionAgent = extractionAgent;
    }

    /// <summary>
    /// 指定した URL に移動してページの本文テキストを取得する。
    /// ページはセッション中開いたままになる。
    /// </summary>
    [Description("サイトのトップページなど入口 URL を直接開きます。検索結果のリンクは BrowserClick で開いてください。")]
    public async Task<string> BrowserNavigate(
        [Description("移動先の入口 URL（サイトのトップページなど。検索結果の URL には使わない）")] string url,
        CancellationToken cancellationToken = default)
    {
        ValidateDomain(url);
        var rawText = await _browser.NavigateAsync(url, cancellationToken);
        var formatted = await FormatIfAvailableAsync(rawText, url, cancellationToken);
        return WithCurrentUrl(formatted);
    }

    /// <summary>
    /// 現在のページで指定テキストの要素をクリックし、遷移・更新後のテキストを取得する。
    /// リンク・ボタン・タブなどあらゆるクリック可能な要素に対応する。
    /// </summary>
    [Description("現在のページで指定テキストの要素をクリックし、結果のページ本文を返します。リンク・ボタン・タブなどに使えます。")]
    public async Task<string> BrowserClick(
        [Description("クリック対象の表示テキスト（リンク文字列やボタンのラベル）")] string text,
        CancellationToken cancellationToken = default)
    {
        string rawText;
        try
        {
            rawText = await _browser.ClickAsync(text, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return $"クリック失敗: {ex.Message}\n別のテキストを指定するか、BrowserGetLinks でリンク一覧を確認してください。";
        }

        var url = _browser.CurrentUrl ?? "";
        if (!string.IsNullOrEmpty(url) && !IsDomainAllowed(url))
        {
            // 許可外ドメインに遷移した場合は戻る
            try { await _browser.GoBackAsync(cancellationToken); } catch { /* best effort */ }
            return $"クリック先のドメインは許可されていません: {url}\nBrowserGoBack で戻りました。別のリンクを選んでください。";
        }

        var formatted = await FormatIfAvailableAsync(rawText, url, cancellationToken);
        return WithCurrentUrl(formatted);
    }

    /// <summary>
    /// 現在のページの本文テキストを再取得する。
    /// 動的コンテンツの再読み込みやクリック後の確認に使う。
    /// </summary>
    [Description("現在開いているページの本文テキストを再取得します。")]
    public async Task<string> BrowserGetPageContent(
        CancellationToken cancellationToken = default)
    {
        var rawText = await _browser.GetPageContentAsync(cancellationToken);
        var url = _browser.CurrentUrl ?? "";
        var formatted = await FormatIfAvailableAsync(rawText, url, cancellationToken);
        return WithCurrentUrl(formatted);
    }

    /// <summary>
    /// 現在のページ内のリンク一覧を抽出する。
    /// </summary>
    [Description("現在のページ内のリンク一覧を取得します。遷移先候補を確認するときに使います。")]
    public async Task<string> BrowserGetLinks(
        [Description("抽出する最大リンク数（既定値 20）")] int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        var links = await _browser.GetLinksAsync(maxResults, cancellationToken);

        if (links.Count == 0)
            return "リンクが見つかりませんでした。";

        var sb = new StringBuilder();
        sb.AppendLine($"現在のページ: {_browser.CurrentUrl ?? "(不明)"}");
        sb.AppendLine();
        foreach (var link in links)
            sb.AppendLine($"- [{link.Title}]({link.Url})");
        return sb.ToString();
    }

    /// <summary>
    /// 検索エンジン（Bing）でクエリを実行し、検索結果ページのテキストを取得する。
    /// 検索後、ブラウザは検索結果ページを表示した状態になるため、
    /// BrowserClick で検索結果のリンクをクリックしてページを開ける。
    /// </summary>
    [Description("Bing で検索し、結果ページのテキストを返します。検索後はそのページに留まるので BrowserClick でリンクを開けます。")]
    public async Task<string> BrowserSearch(
        [Description("検索クエリ（スペース区切りのキーワード）")] string query,
        [Description("検索対象サイトのドメイン（例: www.jra.go.jp）省略可")] string? site = null,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = string.IsNullOrWhiteSpace(site)
            ? query
            : $"{query} site:{site}";

        var rawText = await _browser.SearchAsync(searchQuery, cancellationToken);

        if (string.IsNullOrWhiteSpace(rawText))
            return "検索結果が見つかりませんでした。";

        var formatted = await FormatIfAvailableAsync(rawText, _browser.CurrentUrl ?? "", cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"検索: {searchQuery}");
        sb.AppendLine("※ 結果を開くには BrowserClick(\"リンクのタイトル\") を使ってください。");
        sb.AppendLine();
        sb.AppendLine(formatted);
        return sb.ToString();
    }

    /// <summary>
    /// ブラウザの「戻る」を実行し、前のページのテキストを返す。
    /// </summary>
    [Description("前のページに戻り、その本文テキストを返します。")]
    public async Task<string> BrowserGoBack(
        CancellationToken cancellationToken = default)
    {
        var rawText = await _browser.GoBackAsync(cancellationToken);
        var url = _browser.CurrentUrl ?? "";
        var formatted = await FormatIfAvailableAsync(rawText, url, cancellationToken);
        return WithCurrentUrl(formatted);
    }

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(BrowserNavigate),
        AIFunctionFactory.Create(BrowserClick),
        AIFunctionFactory.Create(BrowserGetPageContent),
        AIFunctionFactory.Create(BrowserGetLinks),
        AIFunctionFactory.Create(BrowserSearch),
        AIFunctionFactory.Create(BrowserGoBack),
    ];

    // ------------------------------------------------------------------ //
    // helpers
    // ------------------------------------------------------------------ //

    private string WithCurrentUrl(string content)
    {
        var url = _browser.CurrentUrl;
        if (string.IsNullOrEmpty(url))
            return content;

        return $"[現在のページ: {url}]\n\n{content}";
    }

    private async Task<string> FormatIfAvailableAsync(
        string rawText, string url, CancellationToken cancellationToken)
    {
        if (_extractionAgent is null)
            return rawText;

        return await _extractionAgent.FormatPageContentAsync(rawText, url, cancellationToken);
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

    private void ValidateDomainIfAbsolute(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            ValidateHost(uri.Host);
        }
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

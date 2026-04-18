using System.ComponentModel;
using System.Text;
using System.Web;
using HorseRacingPrediction.Agents.Browser;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// Playwright を使ってインターネットから競馬情報を取得する Semantic Kernel プラグイン。
/// 各エージェントの Kernel に <c>AddFromObject</c> で登録すると、
/// <c>[KernelFunction]</c> メソッドがツールとして使用可能になる。
/// </summary>
public sealed class WebFetchTools
{
    private readonly IWebBrowser _browser;
    private readonly WebFetchOptions _options;

    public WebFetchTools(IWebBrowser browser, IOptions<WebFetchOptions> options)
    {
        _browser = browser;
        _options = options.Value;
    }

    /// <summary>
    /// 指定した URL のページ本文を取得する。
    /// 許可ドメイン一覧に含まれない URL はアクセスを拒否する。
    /// </summary>
    [KernelFunction]
    [Description("指定した URL のページ本文テキストを取得します。競馬情報サイトの URL を指定してください。")]
    public async Task<string> FetchPageContent(
        [Description("取得対象のページ URL")] string url,
        CancellationToken cancellationToken = default)
    {
        ValidateDomain(url);
        return await _browser.FetchTextAsync(url, cancellationToken);
    }

    /// <summary>
    /// 検索クエリで Bing を検索し、上位ページの本文を取得する。
    /// </summary>
    [KernelFunction]
    [Description("検索クエリで Bing 検索を実行し、上位の検索結果ページの本文テキストを取得します。")]
    public async Task<string> SearchAndFetch(
        [Description("検索クエリ文字列")] string query,
        [Description("検索対象を絞り込むサイト名（省略可。例: site:www.jra.go.jp）")] string? site = null,
        CancellationToken cancellationToken = default)
    {
        var q = string.IsNullOrWhiteSpace(site)
            ? HttpUtility.UrlEncode(query)
            : HttpUtility.UrlEncode($"{query} site:{site}");

        var searchUrl = _options.SearchBaseUrl + q;
        return await _browser.FetchTextAsync(searchUrl, cancellationToken);
    }

    /// <summary>
    /// 指定した競馬場・日付・レース番号の出馬表を取得して Markdown 形式で返す。
    /// </summary>
    [KernelFunction]
    [Description("指定した競馬場・日付・レース番号の出馬表（出走馬情報）を Markdown 表形式で取得します。")]
    public async Task<string> FetchRaceCard(
        [Description("競馬場コード（例: 05 = 東京、06 = 中山）")] string racecourseCode,
        [Description("レース開催日（YYYYMMDD 形式）")] string raceDate,
        [Description("レース番号（1〜12）")] int raceNumber,
        CancellationToken cancellationToken = default)
    {
        var url = BuildJraRaceCardUrl(racecourseCode, raceDate, raceNumber);
        ValidateDomain(url);
        var rawText = await _browser.FetchTextAsync(url, cancellationToken);
        return $"# 出馬表 競馬場={racecourseCode} 日付={raceDate} R{raceNumber}\n\n{rawText}";
    }

    /// <summary>
    /// 指定した馬名で netkeiba を検索し、過去の出走・成績情報を取得する。
    /// </summary>
    [KernelFunction]
    [Description("指定した馬名の過去の出走成績（戦績）を取得します。")]
    public async Task<string> FetchHorseHistory(
        [Description("馬名（日本語可）")] string horseName,
        CancellationToken cancellationToken = default)
    {
        var encoded = HttpUtility.UrlEncode(horseName);
        var url = $"https://db.netkeiba.com/horse/search/?name={encoded}";
        ValidateDomain(url);
        var rawText = await _browser.FetchTextAsync(url, cancellationToken);
        return FormatSection($"馬名「{horseName}」の戦績", rawText);
    }

    /// <summary>
    /// 指定した騎手名の最近の成績・勝率を取得する。
    /// </summary>
    [KernelFunction]
    [Description("指定した騎手名の最近の成績・騎乗数・勝率を取得します。")]
    public async Task<string> FetchJockeyStats(
        [Description("騎手名（日本語可）")] string jockeyName,
        CancellationToken cancellationToken = default)
    {
        var encoded = HttpUtility.UrlEncode(jockeyName);
        var url = $"https://db.netkeiba.com/jockey/search/?name={encoded}";
        ValidateDomain(url);
        var rawText = await _browser.FetchTextAsync(url, cancellationToken);
        return FormatSection($"騎手「{jockeyName}」の成績", rawText);
    }

    // ------------------------------------------------------------------ //
    // private helpers
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

    private static string BuildJraRaceCardUrl(string racecourseCode, string raceDate, int raceNumber)
    {
        // JRA の出馬表 URL パターン (参考: https://www.jra.go.jp/)
        return $"https://www.jra.go.jp/JRADB/accessD.html?" +
               $"CNAME=pw01sde0203_{raceDate}{racecourseCode}{raceNumber:D2}01&sub=";
    }

    private static string FormatSection(string heading, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }
}

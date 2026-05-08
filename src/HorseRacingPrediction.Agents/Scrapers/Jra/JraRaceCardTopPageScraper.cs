using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 競馬メニューまたは今週のレースページから出馬表の開催ボタン一覧を取得するスクレイパー。
/// <para>
/// <see cref="DefaultEntryUrl"/>（https://www.jra.go.jp/keiba/）からアクセスした場合は
/// 「出馬表」ボタンをクリックして開催選択ページへ遷移する。
/// <see cref="ThisWeekEntryUrl"/>（https://www.jra.go.jp/keiba/thisweek/）からアクセスした場合は
/// すでに出馬表一覧ページにいるため、クリック不要でそのまま開催ボタンを抽出する。
/// </para>
/// </summary>
public sealed class JraRaceCardTopPageScraper : IScraper<IReadOnlyList<string>>
{
    /// <summary>JRA 競馬メニューのエントリーポイント。このページから「出馬表」をクリックする。</summary>
    public const string DefaultEntryUrl = "https://www.jra.go.jp/keiba/";

    /// <summary>今週のレース一覧ページ。このURLからはそのまま開催ボタンを抽出できる。</summary>
    public const string ThisWeekEntryUrl = "https://www.jra.go.jp/keiba/thisweek/";

    // 例: 1回東京1日 / 3回京都3日 / 2回阪神2日
    private static readonly Regex HoldingButtonRegex =
        new(@"\d+回(東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)\d+日",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IWebBrowser _browser;

    public JraRaceCardTopPageScraper(IWebBrowser browser)
    {
        _browser = browser;
    }

    /// <summary>
    /// エントリーポイント URL に移動し、必要であれば「出馬表」をクリックして開催選択ページへ遷移した後、
    /// 開催ボタンのラベル一覧（例: "1回東京1日"）を返す。
    /// </summary>
    /// <param name="url">
    /// エントリーポイントURL。
    /// <see cref="DefaultEntryUrl"/>（https://www.jra.go.jp/keiba/）または
    /// <see cref="ThisWeekEntryUrl"/>（https://www.jra.go.jp/keiba/thisweek/）を推奨。
    /// </param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task<IReadOnlyList<string>?> ScrapeAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        await _browser.NavigateAsync(url, cancellationToken);

        // keiba/ のトップメニューからアクセスした場合は「出馬表」をクリックして一覧へ
        // thisweek/ はすでに出馬表ページなのでクリック不要
        var normalizedUrl = url.TrimEnd('/');
        if (normalizedUrl.EndsWith("keiba", StringComparison.OrdinalIgnoreCase))
        {
            await _browser.ClickAsync("出馬表", cancellationToken);
        }

        var snapshot = await _browser.GetPageSnapshotAsync(maxLinks: 1, cancellationToken: cancellationToken);

        var sourceText = string.Join(" ",
            snapshot.Headings
                .Concat(new[] { snapshot.MainText ?? string.Empty })
                .Concat(snapshot.Actions.Select(a => a.Text))
                .Concat(snapshot.Links.Select(l => l.Title)));

        var labels = HoldingButtonRegex.Matches(sourceText)
            .Select(m => m.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return labels.Count > 0 ? labels : null;
    }
}

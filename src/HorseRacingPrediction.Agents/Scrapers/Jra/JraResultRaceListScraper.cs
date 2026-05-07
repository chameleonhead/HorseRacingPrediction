using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA の「開催日+競馬場」単位のレース選択ページから利用可能なレース番号一覧を取得するスクレイパー。
/// <para>
/// ブラウザが既に 開催選択ページ（accessS.html）を表示している状態で呼び出すこと。
/// <paramref name="holdingLabel"/> は開催選択ページ上でクリックするボタンの表示テキスト（例: "3回京都3日"）。
/// クリック後のレース選択ページを解析し、利用可能なレース番号（例: 1, 2, ... 12）を返す。
/// 各レースへの遷移は "1R", "2R" といったボタンのクリックで行う。
/// </para>
/// </summary>
public sealed class JraResultRaceListScraper : IScraper<IReadOnlyList<int>>
{
    // "1レース" ～ "12レース" の形式（画像の alt テキスト）を検出する
    private static readonly Regex RaceNumberRegex =
        new(@"(1[0-2]|[1-9])レース", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IWebBrowser _browser;

    public JraResultRaceListScraper(IWebBrowser browser)
    {
        _browser = browser;
    }

    /// <summary>
    /// 開催選択ページ上の <paramref name="holdingLabel"/> をクリックしてレース選択ページへ遷移し、
    /// 利用可能なレース番号の一覧を返す。
    /// </summary>
    /// <param name="holdingLabel">クリックする開催ボタンの表示テキスト（例: "3回京都3日"）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task<IReadOnlyList<int>?> ScrapeAsync(
        string holdingLabel,
        CancellationToken cancellationToken = default)
    {
        await _browser.ClickAsync(holdingLabel, cancellationToken);
        var snapshot = await _browser.GetPageSnapshotAsync(cancellationToken: cancellationToken);

        // Links.Title に "Xレース" が含まれる（<a><img alt="Xレース">）リンクをカウント
        var combined = string.Join(" ", snapshot.Links.Select(l => l.Title));
        var raceNumbers = RaceNumberRegex.Matches(combined)
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        return raceNumbers.Count > 0 ? raceNumbers : null;
    }
}

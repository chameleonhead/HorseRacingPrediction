using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 成績トップページから「開催日+競馬場」単位の結果ページへのリンクを抽出するスクレイパー。
/// <para>
/// <paramref name="url"/> で指定したエントリーポイント（既定: https://www.jra.go.jp/keiba/）に
/// 移動し、「レース結果」ボタンをクリックすることで 開催選択ページへ遷移する。
/// URL パターンを構築せず、ページに表示されているリンク・ボタンの表示テキストを返す。
/// </para>
/// </summary>
public sealed class JraResultTopPageScraper : IScraper<IReadOnlyList<JraResultDayCourseLink>>
{
    /// <summary>JRA 競馬メニューのエントリーポイント。このページから「レース結果」をクリックする。</summary>
    public const string DefaultEntryUrl = "https://www.jra.go.jp/keiba/";

    private static readonly string[] RacecourseNames =
    [
        "東京", "中山", "阪神", "京都", "中京", "小倉", "函館", "福島", "新潟", "札幌"
    ];

    // 「5月4日」「2026年5月4日」などに対応
    private static readonly Regex MonthDayRegex =
        new(@"(\d{1,2})月(\d{1,2})日", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FullDateRegex =
        new(@"(\d{4})年(\d{1,2})月(\d{1,2})日", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // 例: 3回京都3日 / 2回東京4日
    private static readonly Regex HoldingButtonRegex =
        new(@"\d+回(東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)\d+日", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IWebBrowser _browser;

    public JraResultTopPageScraper(IWebBrowser browser)
    {
        _browser = browser;
    }

    /// <summary>
    /// エントリーポイント URL に移動し、「レース結果」をクリックして 開催選択ページへ遷移した後、
    /// ページ上の各開催日+競馬場リンクを返す。
    /// </summary>
    /// <param name="url">JRA 競馬メニューのエントリーURL（<see cref="DefaultEntryUrl"/> 推奨）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task<IReadOnlyList<JraResultDayCourseLink>?> ScrapeAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        // エントリーポイントから「レース結果」ボタンをクリックして 開催選択ページへ
        await _browser.NavigateAsync(url, cancellationToken);
        await _browser.ClickAsync("レース結果", cancellationToken);

        var snapshot = await _browser.GetPageSnapshotAsync(cancellationToken: cancellationToken);

        var results = ExtractHoldings(snapshot);

        results = results
            .DistinctBy(l => l.Label, StringComparer.Ordinal)
            .OrderBy(l => l.RaceDate)
            .ThenBy(l => l.Racecourse)
            .ToList();

        return results;
    }

    private static List<JraResultDayCourseLink> ExtractHoldings(PageSnapshot snapshot)
    {
        // 改行有無に依存せず、本文・見出し・リンクタイトルを結合して抽出する。
        var sourceText = string.Join(" ",
            snapshot.Headings
                .Concat(new[] { snapshot.MainText ?? string.Empty })
                .Concat(snapshot.Links.Select(l => l.Title)));

        var matches = HoldingButtonRegex.Matches(sourceText);
        var results = new List<JraResultDayCourseLink>();

        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var label = match.Value.Trim();
            var racecourse = RacecourseNames.FirstOrDefault(rc => label.Contains(rc, StringComparison.Ordinal));
            if (racecourse is null)
            {
                continue;
            }

            results.Add(new JraResultDayCourseLink(
                Url: string.Empty,
                Label: label,
                Racecourse: racecourse,
                RaceDate: null));
        }

        return results;
    }

    private static DateOnly? ExtractDate(string text)
    {
        // 年付き（2026年5月4日）
        var fullMatch = FullDateRegex.Match(text);
        if (fullMatch.Success &&
            int.TryParse(fullMatch.Groups[1].Value, out var y) &&
            int.TryParse(fullMatch.Groups[2].Value, out var mo) &&
            int.TryParse(fullMatch.Groups[3].Value, out var d))
        {
            try { return new DateOnly(y, mo, d); }
            catch (ArgumentOutOfRangeException) { }
        }

        // 月日のみ（5月4日）→ 当年として解釈
        var mdMatch = MonthDayRegex.Match(text);
        if (mdMatch.Success &&
            int.TryParse(mdMatch.Groups[1].Value, out var month) &&
            int.TryParse(mdMatch.Groups[2].Value, out var day))
        {
            var year = DateTime.Today.Year;
            try { return new DateOnly(year, month, day); }
            catch (ArgumentOutOfRangeException) { }
        }

        return null;
    }

}

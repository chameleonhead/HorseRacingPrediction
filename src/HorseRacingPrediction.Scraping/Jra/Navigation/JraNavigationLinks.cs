namespace HorseRacingPrediction.Scraping.Jra.Navigation;

/// <summary>
/// リンク文字列を一箇所へ集約する。JRA表記変更時の影響範囲をここへ限定する。
/// レース番号等、ページ固有リンクまではここへ置かない。
/// </summary>
internal static class JraNavigationLinks
{
    public static readonly string[] Calendar =
    [
        "開催日程"
    ];

    public static readonly string[] RaceCard =
    [
        "出馬表"
    ];

    public static readonly string[] RaceResult =
    [
        "レース結果"
    ];

    /// <summary>
    /// 直近の過去開催結果一覧への導線。実ページのリンク文言は未調査であり、
    /// 確認後に固定する（暫定）。
    /// </summary>
    public static readonly string[] RecentRaceResults =
    [
        "過去のレース結果"
    ];

    public static readonly string[] HistoricalRaceSearch =
    [
        "過去レース結果検索"
    ];
}

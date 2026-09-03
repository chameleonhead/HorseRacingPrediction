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

    public static readonly string[] HistoricalRaceSearch =
    [
        "過去レース結果検索"
    ];
}

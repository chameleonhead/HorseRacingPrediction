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
    /// 直近の過去開催結果一覧への導線。
    /// Task 16 実サイトE2Eテストで確認した結果、"過去のレース結果" というテキストの
    /// クリック可能要素は実際の /JRADB/accessS.html（レース結果 開催選択）ページ上には
    /// 存在しないことが判明した（2026-09-04 時点、ClickAsync が
    /// InvalidOperationException を送出することを確認済み）。同ページは直近の週の
    /// 特別・重賞レースへの直接リンクと、過去の開催日（例: "8月23日 （日曜）"）の見出しを
    /// 列挙する構造であり、Task 1-15 で想定していた「"過去のレース結果" という固定リンク→
    /// 日付/競馬場選択」という導線は誤りだった。
    /// 正しい導線（開催日見出しがタブ/アコーディオンなのか別リンクなのか等）は未確定のため、
    /// 値を暫定のまま残し、下流の <see cref="JraNavigator"/> 側にも未解決である旨を
    /// コメントで残す。挙動を推測で書き換えることは避ける。
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

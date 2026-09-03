namespace HorseRacingPrediction.Scraping.Jra.Navigation;

/// <summary>
/// 固定URLを集中管理する。実際のレースページURLはJRAサイト上のリンクから取得するため、
/// ここには固定入口としてのみ利用するURLを置く。
/// </summary>
internal static class JraUrls
{
    public const string KeibaTop =
        "https://www.jra.go.jp/keiba/";

    public const string Calendar =
        "https://www.jra.go.jp/keiba/calendar/";
}

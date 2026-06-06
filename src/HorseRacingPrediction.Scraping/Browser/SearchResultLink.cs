namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// 検索結果から抽出されたリンク情報。
/// </summary>
public sealed record PageLinkSnapshot(string Url, string Title, string Region = "content");

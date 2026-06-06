namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// モデルへ渡すための Web ページ構造スナップショット。
/// </summary>
public sealed record PageSnapshot(
    string Url,
    string? Title,
    string MainText,
    IReadOnlyList<string> Headings,
    IReadOnlyList<PageLinkSnapshot> Links,
    IReadOnlyList<PageActionSnapshot> Actions,
    IReadOnlyList<PageTableSnapshot> Tables);
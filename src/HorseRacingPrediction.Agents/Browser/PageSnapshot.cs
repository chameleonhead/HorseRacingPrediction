namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// モデルへ渡すための Web ページ構造スナップショット。
/// </summary>
public sealed record PageSnapshot(
    string Url,
    string? Title,
    string MainText,
    IReadOnlyList<string> Headings,
    IReadOnlyList<SearchResultLink> Links,
    IReadOnlyList<PageActionSnapshot> Actions,
    IReadOnlyList<PageTableSnapshot> Tables);
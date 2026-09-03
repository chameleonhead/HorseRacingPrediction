// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Scraping.Workflow;

/// <summary>
/// <see cref="JraRaceCardCollectionWorkflow.CollectAsync"/> の実行結果。
/// </summary>
public sealed record JraRaceCardCollectionResult(
    /// <summary>収集対象の週末日付</summary>
    DateOnly WeekendDate,
    /// <summary>エージェントが発見した出馬表 URL 一覧</summary>
    IReadOnlyList<JraRaceCardUrl> DiscoveredUrls,
    /// <summary>スクレイピングに成功した出馬表データ一覧</summary>
    IReadOnlyList<JraRaceCardData> ScrapedCards,
    /// <summary>DB 保存に成功したレース ID 一覧</summary>
    IReadOnlyList<string> SavedRaceIds,
    /// <summary>スクレイピングまたは保存で発生したエラーメッセージ一覧</summary>
    IReadOnlyList<string> Errors);
#endif

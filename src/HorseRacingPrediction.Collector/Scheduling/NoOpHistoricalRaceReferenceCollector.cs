namespace HorseRacingPrediction.Collector.Scheduling;

/// <summary>
/// JRAサイト再設計（docs/jra-scraping.md）に伴い、旧URL列挙方式に依存していた
/// <see cref="JraHistoricalRaceReferenceCollector"/>（過去レース結果の探索）は削除済みで、
/// 新Jra層での再実装は別タスクの範囲となる。
/// 一方 <see cref="HistoricalDataRequestPlanner"/> は本コレクターを必須依存として要求するため、
/// DI未解決でCollectorが起動時にクラッシュしないよう、常に空リストを返す暫定実装を登録する。
/// これにより「過去レース結果の補完要求」機能のみが恒常的にスキップされ、
/// 馬・騎手・調教師のプロフィール補完（<see cref="HistoricalDataRequestPlanner.EnsureRequestsForRaceAsync"/>
/// 内の EnsureEntityHistoryRequestsAsync）には影響しない。
/// </summary>
public sealed class NoOpHistoricalRaceReferenceCollector : IHistoricalRaceReferenceCollector
{
    public Task<IReadOnlyList<HistoricalRaceReference>> CollectAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<HistoricalRaceReference>>(Array.Empty<HistoricalRaceReference>());
}

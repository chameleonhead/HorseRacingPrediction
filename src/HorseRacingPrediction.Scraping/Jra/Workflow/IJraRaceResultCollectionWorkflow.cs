using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// 指定レースの確定結果（着順）を収集し、書き込みサービスへ登録するワークフロー。
/// </summary>
public interface IJraRaceResultCollectionWorkflow
{
    Task<RaceResultCollectionResult> CollectAsync(
        RaceId raceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase8: 同日・同競馬場の複数レースを連続して収集する呼び出し元向けの最適化。
    /// <paramref name="useSiblingNavigation"/>にtrueを渡すと、ブラウザが既に
    /// 直前のレース結果ページを表示している前提で
    /// <see cref="Jra.Navigation.IJraNavigator.ToSiblingRaceResultAsync"/>
    /// （現在ページからの直接遷移、失敗時は自動的にフルパスへフォールバック）を使う。
    /// falseの場合は<see cref="CollectAsync(RaceId, CancellationToken)"/>と同じ
    /// 挙動（常にフルパス）になる。
    /// </summary>
    Task<RaceResultCollectionResult> CollectAsync(
        RaceId raceId,
        bool useSiblingNavigation,
        CancellationToken cancellationToken = default);
}

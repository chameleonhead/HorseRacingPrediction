using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// 指定レースの確定結果（着順）を収集し、書き込みサービスへ登録するワークフロー。
/// </summary>
public interface IJraRaceResultCollectionWorkflow
{
    Task<RaceResultCollectionResult> RefreshAsync(RaceId raceId, string targetRaceId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Single race refresh is not supported.");

    Task<RaceResultCollectionResult> CollectAsync(
        RaceId raceId,
        CancellationToken cancellationToken = default);
}

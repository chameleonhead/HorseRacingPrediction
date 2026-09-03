using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// 指定した開催日・競馬場のレース一覧を取得し、各レースの出馬表を収集して
/// <see cref="HorseRacingPrediction.ApiClient.IDataCollectionWriteService"/> 経由で保存するワークフロー。
/// </summary>
public interface IJraRaceCardCollectionWorkflow
{
    /// <summary>
    /// 指定日・競馬場のレース一覧を取得し、各レースの出馬表を収集・保存する。
    /// 個別レースの取得・保存に失敗した場合はそのレースをスキップして処理を続行する
    /// （Workflow自体でリトライは行わない。リトライ方針は呼び出し側のJob/ProcessingStateに従う）。
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="course"/> が <see cref="RaceCourse.Unknown"/> の場合。
    /// </exception>
    /// <exception cref="JraCollectionException">
    /// レース一覧ページを取得できなかった場合。
    /// </exception>
    Task<RaceCardCollectionResult> CollectAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken = default);
}

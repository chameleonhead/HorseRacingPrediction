using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// 指定日にJRAで開催される競馬場の一覧を収集するワークフロー。
/// </summary>
public interface IJraScheduleCollectionWorkflow
{
    /// <summary>
    /// 指定日のカレンダーを参照し、その日に開催される競馬場の一覧を返す。
    /// その日に開催がなければ空配列を返す。
    /// </summary>
    /// <exception cref="JraCollectionException">
    /// カレンダーページを取得できなかった場合。
    /// </exception>
    Task<IReadOnlyList<RaceCourse>> CollectAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}

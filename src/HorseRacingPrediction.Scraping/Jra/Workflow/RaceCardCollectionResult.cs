using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// <see cref="IJraRaceCardCollectionWorkflow.CollectAsync"/> の実行結果。
/// </summary>
/// <param name="Date">収集対象の開催日</param>
/// <param name="Course">収集対象の競馬場</param>
/// <param name="RaceIds">保存に成功したレース ID 一覧（開催日・競馬場内のレース番号順）</param>
/// <param name="Errors">
/// 個別レースの取得・保存に失敗した際のエラーメッセージ一覧。
/// 1レースの失敗で全体を止めず、残りのレースの処理を続行する（部分成功を許容する）。
/// </param>
public sealed record RaceCardCollectionResult(
    DateOnly Date,
    RaceCourse Course,
    IReadOnlyList<string> RaceIds,
    IReadOnlyList<string> Errors);

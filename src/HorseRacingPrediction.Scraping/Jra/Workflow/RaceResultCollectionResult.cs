using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// <see cref="IJraRaceResultCollectionWorkflow.CollectAsync"/> の実行結果。
/// </summary>
/// <param name="RaceId">収集対象の JRA レース識別子（日付・競馬場・レース番号）</param>
/// <param name="DataCollectionRaceId">
/// <see cref="ApiClient.DeterministicIdGenerator.BuildRaceId"/> で算出した書き込みサービス側のレース ID。
/// </param>
/// <param name="SavedHorseNumbers">着順の記録に成功した馬番一覧（着順ページ掲載順）</param>
/// <param name="Errors">
/// 個別エントリーの記録に失敗した際のエラーメッセージ一覧。
/// 1エントリーの失敗で全体を止めず、残りのエントリーの処理を続行する（部分成功を許容する）。
/// </param>
/// <param name="SourceUrl">取得元のJRAレース結果ページURL（取得できた場合）。引用元として記録に残す。</param>
public sealed record RaceResultCollectionResult(
    RaceId RaceId,
    string DataCollectionRaceId,
    IReadOnlyList<int> SavedHorseNumbers,
    IReadOnlyList<string> Errors,
    string? SourceUrl = null);

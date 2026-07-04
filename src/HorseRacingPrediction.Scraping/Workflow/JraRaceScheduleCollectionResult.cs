namespace HorseRacingPrediction.Scraping.Workflow;

/// <summary>
/// <see cref="JraRaceScheduleCollectionWorkflow"/> の実行結果。
/// </summary>
public sealed record JraRaceScheduleCollectionResult(
    /// <summary>収集時の基準日</summary>
    DateOnly ReferenceDate,
    /// <summary>収集できた開催日（昇順）</summary>
    IReadOnlyList<DateOnly> RaceDates,
    /// <summary>基準日以降の開催日（昇順）</summary>
    IReadOnlyList<DateOnly> UpcomingRaceDates,
    /// <summary>エラーメッセージ。成功時は null。</summary>
    string? Error);

namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// レース結果の1頭分の情報。
/// <see cref="FinishPosition"/> は<see cref="ResultStatus"/>がFinished以外の場合はnullとなる
/// （依頼書17節）。降着があった場合、確定後の着順は<see cref="FinishPosition"/>に、
/// 元の入線順位は<see cref="OriginalFinishPosition"/>に保持する（依頼書18節）。
/// </summary>
public sealed record RaceResultEntry(
    ResultStatus ResultStatus,
    int? FinishPosition,
    int HorseNumber,
    string HorseName,
    string? JockeyName,
    TimeSpan? Time,
    int? OriginalFinishPosition = null,
    HorseSex? Sex = null,
    int? Age = null,
    int? FrameNumber = null,
    decimal? AssignedWeight = null,
    string? TrainerName = null,
    int? Popularity = null,
    int? BodyWeight = null,
    int? BodyWeightChange = null,
    string? MarginRaw = null);

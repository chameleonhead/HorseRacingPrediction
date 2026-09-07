namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// レース結果の1頭分の情報。
/// <see cref="FinishPosition"/> は<see cref="ResultStatus"/>がFinished以外の場合はnullとなる
/// （依頼書17節）。降着があった場合、確定後の着順は<see cref="FinishPosition"/>に、
/// 元の入線順位は<see cref="OriginalFinishPosition"/>に保持する（依頼書18節）。
/// <see cref="MarginRaw"/>は着差を数値正規化せず生文字列のまま保持する（依頼書20節）。
/// <see cref="IsDeadHeat"/>は着差欄が「同着」であったことを示す。
/// <see cref="EstimatedLast3F"/>（推定上り）は平地レース、<see cref="Average1F"/>（平均1F）は
/// 障害レースでのみ値を持ち得る（依頼書21節）。互いに排他ではなく、単に列自体が
/// 存在しないレース種別側は常にnullとなる。
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
    string? MarginRaw = null,
    bool IsDeadHeat = false,
    decimal? EstimatedLast3F = null,
    decimal? Average1F = null,
    // Phase8: 着順テーブル内の「コーナー通過順位」列（馬ごとの半角スペース区切り
    // 整数列、例:「4 3 3 2」=1〜4コーナーの通過順位）。ページ下方の集計テーブル
    // （<see cref="JraRaceResultPage.CornerPassages"/>、<see cref="CornerPassage"/>型）
    // とは別物。列自体が存在しない場合はnull（正常）。
    IReadOnlyList<int>? CornerOrders = null);

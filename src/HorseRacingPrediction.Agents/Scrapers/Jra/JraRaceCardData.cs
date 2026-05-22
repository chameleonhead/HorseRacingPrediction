namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 出馬表ページから抽出した全体データ。
/// レースのメタ情報と出走馬エントリ一覧を保持する。
/// </summary>
public sealed record JraRaceCardData(
    string Url,
    string RaceName,
    string? Racecourse,
    DateOnly? RaceDate,
    int? RaceNumber,
    int? MeetingNumber,
    int? DayNumber,
    TimeOnly? PostTime,
    string? ConditionSummary,
    string? AgeCondition,
    string? AgeConditionCode,
    string? RaceClass,
    string? RaceClassCode,
    string? Eligibility,
    IReadOnlyList<string> EligibilityCodes,
    string? EntryCondition,
    IReadOnlyList<string> EntryConditionCodes,
    string? WeightCondition,
    string? WeightConditionCode,
    string? CourseType,
    string? TrackDirection,
    int? Distance,
    string? Grade,
    IReadOnlyList<JraRacePrizeData> PrizeMoney,
    IReadOnlyList<JraRaceEntryData> Entries);

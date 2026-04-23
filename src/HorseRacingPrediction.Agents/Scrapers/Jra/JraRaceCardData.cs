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
    string? CourseType,
    int? Distance,
    string? Grade,
    IReadOnlyList<JraRaceEntryData> Entries);

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 成績ページから抽出した全体データ。
/// レースのメタ情報・着順エントリ一覧・払い戻しデータを保持する。
/// </summary>
public sealed record JraRaceResultData(
    string Url,
    string RaceName,
    string? Racecourse,
    DateOnly? RaceDate,
    int? RaceNumber,
    string? CourseType,
    int? Distance,
    string? Grade,
    IReadOnlyList<JraRaceResultEntryData> Entries,
    JraRacePayoutData? Payouts);

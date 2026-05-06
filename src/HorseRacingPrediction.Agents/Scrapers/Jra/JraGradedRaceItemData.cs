namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 重賞一覧ページの1レース分データ。
/// </summary>
public sealed record JraGradedRaceItemData(
    DateOnly? RaceDate,
    string DateText,
    string? Weekday,
    string Grade,
    string RaceName,
    string Racecourse,
    string? Conditions,
    string? Course,
    string? WinnerHorse,
    string? WinnerJockey,
    string? ResultUrl);

namespace HorseRacingPrediction.Scraping.Scrapers.Jra;

/// <summary>
/// 競走馬情報ページ（accessU.html）の「競走成績」テーブルから抽出した過去1走分のデータ。
/// </summary>
public sealed record JraHorseRaceHistoryEntryData(
    DateOnly? RaceDate,
    string? Racecourse,
    int? RaceNumber,
    string? RaceName,
    int? GateNumber,
    int? HorseNumber,
    int? FinishPosition,
    string? AbnormalResultCode,
    string? JockeyName,
    decimal? AssignedWeight,
    string? SurfaceCode,
    int? DistanceMeters,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    decimal? BodyWeight,
    decimal? BodyWeightDiff,
    string? WinnerOrRunnerUpHorseName,
    decimal? PrizeMoney);

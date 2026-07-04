namespace HorseRacingPrediction.Scraping.JraNavigation;

/// <summary>JRA レース結果ページから抽出したデータ。</summary>
public sealed record JraRaceResultSummary(
    string? RaceName,
    DateOnly? RaceDate,
    string? Racecourse,
    int? RaceNumber,
    string? GradeCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    IReadOnlyList<JraResultEntry> Entries,
    IReadOnlyList<JraPayoutSummary> Payouts,
    string SourceUrl);
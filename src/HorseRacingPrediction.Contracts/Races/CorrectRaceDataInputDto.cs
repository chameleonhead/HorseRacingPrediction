namespace HorseRacingPrediction.Contracts.Races;

public sealed record CorrectRaceDataInputDto(
    string? RaceName,
    string? RacecourseCode,
    int? RaceNumber,
    string? GradeCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    string? Reason);

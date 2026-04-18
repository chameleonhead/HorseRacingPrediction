namespace HorseRacingPrediction.Api.Contracts;

public sealed record CorrectRaceDataRequest(
    string? RaceName,
    string? RacecourseCode,
    int? RaceNumber,
    string? GradeCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    string? Reason);

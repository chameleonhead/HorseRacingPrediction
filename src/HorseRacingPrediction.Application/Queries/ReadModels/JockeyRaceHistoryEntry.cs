namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record JockeyRaceHistoryEntry(
    string RaceId,
    string EntryId,
    string HorseId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    string? GradeCode,
    int? FinishPosition,
    decimal? PrizeMoney);

namespace HorseRacingPrediction.Contracts;

public sealed record JockeyRaceHistoryEntryDto(
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
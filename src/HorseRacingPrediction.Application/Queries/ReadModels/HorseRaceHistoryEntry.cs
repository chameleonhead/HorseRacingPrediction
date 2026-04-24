namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record HorseRaceHistoryEntry(
    string RaceId,
    string EntryId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    string? GradeCode,
    int? GateNumber,
    decimal? AssignedWeight,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? RunningStyleCode,
    string? JockeyId,
    string? TrainerId,
    int? FinishPosition,
    string? LastThreeFurlongTime,
    string? CornerPositions,
    decimal? PrizeMoney);

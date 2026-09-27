namespace HorseRacingPrediction.Contracts;

public sealed record RaceSearchSummaryDto(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber);
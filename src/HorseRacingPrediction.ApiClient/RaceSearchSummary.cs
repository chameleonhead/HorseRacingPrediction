namespace HorseRacingPrediction.ApiClient;

public sealed record RaceSearchSummary(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber);
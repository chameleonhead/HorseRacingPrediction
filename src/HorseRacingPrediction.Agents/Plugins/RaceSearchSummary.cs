namespace HorseRacingPrediction.Agents.Plugins;

public sealed record RaceSearchSummary(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber);
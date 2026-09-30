namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceResultBulkItemOutcomeDto(
    string Scope,
    string Key,
    string Status,
    string? ErrorCode = null,
    string? Message = null);

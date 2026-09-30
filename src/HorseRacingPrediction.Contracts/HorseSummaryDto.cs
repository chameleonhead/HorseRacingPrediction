namespace HorseRacingPrediction.Contracts;

public sealed record HorseSummaryDto(
    string HorseId,
    string RegisteredName,
    string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    int AliasCount);
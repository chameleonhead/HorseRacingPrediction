namespace HorseRacingPrediction.Api.Contracts;

public sealed record HorseSummaryResponse(
    string HorseId,
    string RegisteredName,
    string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    int AliasCount);
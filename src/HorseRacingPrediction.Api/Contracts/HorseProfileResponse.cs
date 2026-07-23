namespace HorseRacingPrediction.Api.Contracts;

public sealed record HorseProfileResponse(
    string HorseId,
    string RegisteredName,
    string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    IReadOnlyList<AliasResponse> Aliases,
    string? OwnerName = null);

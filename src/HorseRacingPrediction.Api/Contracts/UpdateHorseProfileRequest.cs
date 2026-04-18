namespace HorseRacingPrediction.Api.Contracts;

public sealed record UpdateHorseProfileRequest(
    string? RegisteredName,
    string? NormalizedName,
    string? SexCode,
    DateOnly? BirthDate);

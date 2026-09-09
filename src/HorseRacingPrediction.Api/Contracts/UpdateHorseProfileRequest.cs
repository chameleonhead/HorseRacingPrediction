namespace HorseRacingPrediction.Api.Contracts;

public sealed record UpdateHorseProfileRequest(
    string? RegisteredName,
    string? NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    string? OwnerName = null,
    string? BreederName = null,
    string? SireName = null,
    string? DamName = null,
    string? DamsireName = null,
    string? CoatColor = null);

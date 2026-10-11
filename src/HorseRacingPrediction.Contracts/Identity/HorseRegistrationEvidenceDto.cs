namespace HorseRacingPrediction.Contracts.Identity;

public sealed record HorseRegistrationEvidenceDto(
    string Status,
    string? RegisteredName,
    string? NormalizedName,
    DateOnly? BirthDate);

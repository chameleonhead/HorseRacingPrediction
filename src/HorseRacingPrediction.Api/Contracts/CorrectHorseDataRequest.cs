namespace HorseRacingPrediction.Api.Contracts;

public sealed record CorrectHorseDataRequest(
    string? RegisteredName,
    string? NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    string? Reason);

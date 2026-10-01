namespace HorseRacingPrediction.Contracts.Horses;

public sealed record CorrectHorseDataInputDto(
    string? RegisteredName,
    string? NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    string? Reason);

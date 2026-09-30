
namespace HorseRacingPrediction.Contracts.Horses;

public sealed record CorrectHorseDataRequest(
    string? RegisteredName,
    string? NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    string? Reason);

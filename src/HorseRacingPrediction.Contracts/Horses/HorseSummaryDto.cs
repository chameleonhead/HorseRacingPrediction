
namespace HorseRacingPrediction.Contracts.Horses;

public sealed record HorseSummaryDto(
    string HorseId,
    string RegisteredName,
    string NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    int AliasCount);

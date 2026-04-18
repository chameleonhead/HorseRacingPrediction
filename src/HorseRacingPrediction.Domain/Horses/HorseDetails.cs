namespace HorseRacingPrediction.Domain.Horses;

public sealed record HorseDetails(
    string HorseId,
    string? RegisteredName,
    string? NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    IReadOnlyCollection<AliasDetails> Aliases);

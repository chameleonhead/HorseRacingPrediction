namespace HorseRacingPrediction.Domain.Horses;

public sealed record HorseDetails(
    string HorseId,
    string? RegisteredName,
    string? NormalizedName,
    string? SexCode,
    DateOnly? BirthDate,
    string? OwnerName,
    string? BreederName,
    string? SireName,
    string? DamName,
    string? DamsireName,
    string? CoatColor,
    IReadOnlyCollection<AliasDetails> Aliases);

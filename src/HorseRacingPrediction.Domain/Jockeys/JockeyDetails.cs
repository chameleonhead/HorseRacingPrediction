namespace HorseRacingPrediction.Domain.Jockeys;

public sealed record JockeyDetails(
    string JockeyId,
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode,
    IReadOnlyCollection<AliasDetails> Aliases);

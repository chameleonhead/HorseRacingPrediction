namespace HorseRacingPrediction.Contracts;

public sealed record JockeyAliasEntry(
    string AliasType,
    string AliasValue,
    string? SourceName,
    bool IsPrimary);
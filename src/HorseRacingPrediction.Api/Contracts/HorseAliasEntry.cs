namespace HorseRacingPrediction.Api.Contracts;

public sealed record HorseAliasEntry(
    string AliasType,
    string AliasValue,
    string? SourceName,
    bool IsPrimary);
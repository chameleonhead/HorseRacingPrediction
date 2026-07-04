namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed record HorseAliasEntry(
    string AliasType,
    string AliasValue,
    string? SourceName,
    bool IsPrimary);
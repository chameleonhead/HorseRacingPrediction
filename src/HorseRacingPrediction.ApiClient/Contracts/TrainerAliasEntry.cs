namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed record TrainerAliasEntry(
    string AliasType,
    string AliasValue,
    string? SourceName,
    bool IsPrimary);
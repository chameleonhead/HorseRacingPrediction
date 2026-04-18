namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record HorseAliasEntry(
    string AliasType,
    string AliasValue,
    string SourceName,
    bool IsPrimary);

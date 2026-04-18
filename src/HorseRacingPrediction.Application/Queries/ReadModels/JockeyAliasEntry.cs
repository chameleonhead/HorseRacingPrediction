namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record JockeyAliasEntry(
    string AliasType,
    string AliasValue,
    string SourceName,
    bool IsPrimary);

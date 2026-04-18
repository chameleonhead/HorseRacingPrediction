namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record TrainerAliasEntry(
    string AliasType,
    string AliasValue,
    string SourceName,
    bool IsPrimary);


namespace HorseRacingPrediction.Contracts.Common;

public sealed record TrainerAliasEntry(
    string AliasType,
    string AliasValue,
    string? SourceName,
    bool IsPrimary);

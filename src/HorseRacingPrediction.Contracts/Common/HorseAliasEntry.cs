
namespace HorseRacingPrediction.Contracts.Common;

public sealed record HorseAliasEntry(
    string AliasType,
    string AliasValue,
    string? SourceName,
    bool IsPrimary);

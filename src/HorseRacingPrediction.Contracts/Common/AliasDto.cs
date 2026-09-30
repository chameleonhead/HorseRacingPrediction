
namespace HorseRacingPrediction.Contracts.Common;

public sealed record AliasDto(
    string AliasType,
    string AliasValue,
    string SourceName,
    bool IsPrimary);

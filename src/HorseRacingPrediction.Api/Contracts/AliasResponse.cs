namespace HorseRacingPrediction.Api.Contracts;

public sealed record AliasResponse(
    string AliasType,
    string AliasValue,
    string SourceName,
    bool IsPrimary);

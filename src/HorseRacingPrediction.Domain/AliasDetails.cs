namespace HorseRacingPrediction.Domain;

public sealed record AliasDetails(
    string AliasType,
    string AliasValue,
    string SourceName,
    bool IsPrimary);

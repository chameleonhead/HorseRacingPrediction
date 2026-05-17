namespace HorseRacingPrediction.Api.Contracts;

public sealed record RacePlaceOddsResponse(
    int HorseNumber,
    string? HorseName,
    decimal? OddsMin,
    decimal? OddsMax);

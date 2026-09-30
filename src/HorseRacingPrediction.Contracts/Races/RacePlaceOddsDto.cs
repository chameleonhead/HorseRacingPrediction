
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RacePlaceOddsDto(
    int HorseNumber,
    string? HorseName,
    decimal? OddsMin,
    decimal? OddsMax);

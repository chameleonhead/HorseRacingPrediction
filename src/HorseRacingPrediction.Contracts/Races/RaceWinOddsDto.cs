
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceWinOddsDto(
    int HorseNumber,
    string? HorseName,
    decimal? Odds,
    int? Popularity);

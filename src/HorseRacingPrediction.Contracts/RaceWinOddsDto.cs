namespace HorseRacingPrediction.Contracts;

public sealed record RaceWinOddsDto(
    int HorseNumber,
    string? HorseName,
    decimal? Odds,
    int? Popularity);

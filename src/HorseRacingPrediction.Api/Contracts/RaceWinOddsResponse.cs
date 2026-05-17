namespace HorseRacingPrediction.Api.Contracts;

public sealed record RaceWinOddsResponse(
    int HorseNumber,
    string? HorseName,
    decimal? Odds,
    int? Popularity);

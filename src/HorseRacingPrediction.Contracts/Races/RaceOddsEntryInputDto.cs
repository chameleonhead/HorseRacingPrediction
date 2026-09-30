
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceOddsEntryInputDto(int HorseNumber, decimal WinOdds, int? Popularity = null);

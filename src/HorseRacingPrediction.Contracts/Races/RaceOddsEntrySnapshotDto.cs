namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceOddsEntrySnapshotDto(int HorseNumber, decimal WinOdds, int? Popularity);

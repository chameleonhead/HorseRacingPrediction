namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceOddsObservationSnapshotDto(string Market, string Selection, decimal Value, int? Popularity = null);

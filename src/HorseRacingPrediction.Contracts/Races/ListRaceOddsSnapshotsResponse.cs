namespace HorseRacingPrediction.Contracts.Races;

public sealed record ListRaceOddsSnapshotsResponse(IReadOnlyList<RaceOddsSnapshotDto> OddsSnapshots);

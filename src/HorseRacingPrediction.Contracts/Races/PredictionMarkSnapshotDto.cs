namespace HorseRacingPrediction.Contracts.Races;

public sealed record PredictionMarkSnapshotDto(string EntryId, string MarkCode, int PredictedRank, decimal Score,
    string? Comment);

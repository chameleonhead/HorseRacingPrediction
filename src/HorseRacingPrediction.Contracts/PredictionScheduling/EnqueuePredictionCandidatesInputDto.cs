namespace HorseRacingPrediction.Contracts.PredictionScheduling;

public sealed record EnqueuePredictionCandidatesInputDto(string[]? RaceIds, DateTimeOffset Now);

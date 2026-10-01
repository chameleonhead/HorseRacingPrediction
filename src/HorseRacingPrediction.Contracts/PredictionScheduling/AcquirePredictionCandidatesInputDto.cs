namespace HorseRacingPrediction.Contracts.PredictionScheduling;

public sealed record AcquirePredictionCandidatesInputDto(DateTimeOffset Now, TimeSpan MinAge, int MaxCount, TimeSpan LeaseDuration);

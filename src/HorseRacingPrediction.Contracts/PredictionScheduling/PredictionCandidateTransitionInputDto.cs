namespace HorseRacingPrediction.Contracts.PredictionScheduling;

public sealed record PredictionCandidateTransitionInputDto(
    string Mode,
    string LeaseToken,
    DateTimeOffset? AvailableAt = null,
    string? Error = null);

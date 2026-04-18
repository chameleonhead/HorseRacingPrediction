namespace HorseRacingPrediction.Domain.Predictions;

public sealed record PredictionRationaleDetails(
    string SubjectType,
    string SubjectId,
    string SignalType,
    string? SignalValue,
    string? ExplanationText);

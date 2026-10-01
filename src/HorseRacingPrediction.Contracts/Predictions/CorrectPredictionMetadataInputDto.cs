namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record CorrectPredictionMetadataInputDto(
    decimal? ConfidenceScore,
    string? SummaryComment,
    string? Reason);

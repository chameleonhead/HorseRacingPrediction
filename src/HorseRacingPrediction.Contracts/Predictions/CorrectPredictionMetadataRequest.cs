
namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record CorrectPredictionMetadataRequest(
    decimal? ConfidenceScore,
    string? SummaryComment,
    string? Reason);

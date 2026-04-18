namespace HorseRacingPrediction.Api.Contracts;

public sealed record CorrectPredictionMetadataRequest(
    decimal? ConfidenceScore,
    string? SummaryComment,
    string? Reason);

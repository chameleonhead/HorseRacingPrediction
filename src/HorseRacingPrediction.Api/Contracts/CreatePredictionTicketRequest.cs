using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record CreatePredictionTicketRequest(
    [property: Required, StringLength(64, MinimumLength = 1)] string RaceId,
    [property: Required, StringLength(32, MinimumLength = 1)] string PredictorType,
    [property: Required, StringLength(64, MinimumLength = 1)] string PredictorId,
    [property: Range(0, 1)] decimal ConfidenceScore,
    [property: StringLength(300)] string? SummaryComment);

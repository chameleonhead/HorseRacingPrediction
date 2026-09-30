using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record EvaluatePredictionTicketRequest(
    [property: Required] string RaceId,
    DateTimeOffset EvaluatedAt,
    int EvaluationRevision,
    [property: Required] IReadOnlyList<string> HitTypeCodes,
    decimal? ScoreSummary,
    decimal? ReturnAmount,
    decimal? Roi);

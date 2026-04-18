using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record AddPredictionRationaleRequest(
    [property: Required, StringLength(32, MinimumLength = 1)] string SubjectType,
    [property: Required, StringLength(64, MinimumLength = 1)] string SubjectId,
    [property: Required, StringLength(64, MinimumLength = 1)] string SignalType,
    [property: StringLength(200)] string? SignalValue,
    [property: StringLength(500)] string? ExplanationText);

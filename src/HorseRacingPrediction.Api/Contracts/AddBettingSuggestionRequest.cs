using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record AddBettingSuggestionRequest(
    [property: Required, StringLength(16, MinimumLength = 1)] string BetTypeCode,
    [property: Required, StringLength(200, MinimumLength = 1)] string SelectionExpression,
    [property: Range(0, double.MaxValue)] decimal? StakeAmount,
    decimal? ExpectedValue);

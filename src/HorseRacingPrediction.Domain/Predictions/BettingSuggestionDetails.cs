namespace HorseRacingPrediction.Domain.Predictions;

public sealed record BettingSuggestionDetails(
    string BetTypeCode,
    string SelectionExpression,
    decimal? StakeAmount,
    decimal? ExpectedValue);

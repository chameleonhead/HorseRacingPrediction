using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class BettingSuggestionAdded : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public BettingSuggestionAdded(string betTypeCode, string selectionExpression,
        decimal? stakeAmount = null, decimal? expectedValue = null)
    {
        BetTypeCode = betTypeCode;
        SelectionExpression = selectionExpression;
        StakeAmount = stakeAmount;
        ExpectedValue = expectedValue;
    }

    public string BetTypeCode { get; }
    public string SelectionExpression { get; }
    public decimal? StakeAmount { get; }
    public decimal? ExpectedValue { get; }
}

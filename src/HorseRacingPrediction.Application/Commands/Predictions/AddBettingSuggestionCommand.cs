using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class AddBettingSuggestionCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public AddBettingSuggestionCommand(PredictionTicketId aggregateId,
        string betTypeCode, string selectionExpression,
        decimal? stakeAmount = null, decimal? expectedValue = null)
        : base(aggregateId)
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

using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class WithdrawPredictionTicketCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public WithdrawPredictionTicketCommand(PredictionTicketId aggregateId, string? reason = null)
        : base(aggregateId)
    {
        Reason = reason;
    }

    public string? Reason { get; }
}

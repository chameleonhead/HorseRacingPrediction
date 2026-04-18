using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class FinalizePredictionTicketCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public FinalizePredictionTicketCommand(PredictionTicketId aggregateId) : base(aggregateId) { }
}

using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class FinalizePredictionTicketCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, FinalizePredictionTicketCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, FinalizePredictionTicketCommand command, CancellationToken cancellationToken)
    {
        aggregate.FinalizeTicket();
        return Task.CompletedTask;
    }
}

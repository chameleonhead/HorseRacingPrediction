using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class CreatePredictionTicketCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, CreatePredictionTicketCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, CreatePredictionTicketCommand command, CancellationToken cancellationToken)
    {
        aggregate.Create(command.RaceId, command.PredictorType, command.PredictorId,
            command.ConfidenceScore, command.SummaryComment);
        return Task.CompletedTask;
    }
}

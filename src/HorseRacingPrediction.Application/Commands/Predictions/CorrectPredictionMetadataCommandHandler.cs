using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class CorrectPredictionMetadataCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, CorrectPredictionMetadataCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, CorrectPredictionMetadataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectMetadata(command.ConfidenceScore, command.SummaryComment, command.Reason);
        return Task.CompletedTask;
    }
}

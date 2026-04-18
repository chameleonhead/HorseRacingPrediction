using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class AddPredictionMarkCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, AddPredictionMarkCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, AddPredictionMarkCommand command, CancellationToken cancellationToken)
    {
        aggregate.AddMark(command.EntryId, command.MarkCode, command.PredictedRank, command.Score, command.Comment);
        return Task.CompletedTask;
    }
}

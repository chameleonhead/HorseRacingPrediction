using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class AddPredictionRationaleCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, AddPredictionRationaleCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, AddPredictionRationaleCommand command, CancellationToken cancellationToken)
    {
        aggregate.AddRationale(command.SubjectType, command.SubjectId, command.SignalType,
            command.SignalValue, command.ExplanationText);
        return Task.CompletedTask;
    }
}

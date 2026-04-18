using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class EvaluatePredictionTicketCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, EvaluatePredictionTicketCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, EvaluatePredictionTicketCommand command, CancellationToken cancellationToken)
    {
        aggregate.Evaluate(command.RaceId, command.EvaluatedAt, command.EvaluationRevision,
            command.HitTypeCodes, command.ScoreSummary, command.ReturnAmount, command.Roi);
        return Task.CompletedTask;
    }
}

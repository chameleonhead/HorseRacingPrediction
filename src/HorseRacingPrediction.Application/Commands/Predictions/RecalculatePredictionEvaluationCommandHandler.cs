using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class RecalculatePredictionEvaluationCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, RecalculatePredictionEvaluationCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, RecalculatePredictionEvaluationCommand command, CancellationToken cancellationToken)
    {
        aggregate.RecalculateEvaluation(command.RaceId, command.EvaluatedAt, command.EvaluationRevision,
            command.HitTypeCodes, command.ScoreSummary, command.ReturnAmount, command.Roi);
        return Task.CompletedTask;
    }
}

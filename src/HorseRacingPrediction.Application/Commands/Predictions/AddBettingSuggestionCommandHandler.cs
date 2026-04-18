using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class AddBettingSuggestionCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, AddBettingSuggestionCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, AddBettingSuggestionCommand command, CancellationToken cancellationToken)
    {
        aggregate.AddBettingSuggestion(command.BetTypeCode, command.SelectionExpression,
            command.StakeAmount, command.ExpectedValue);
        return Task.CompletedTask;
    }
}

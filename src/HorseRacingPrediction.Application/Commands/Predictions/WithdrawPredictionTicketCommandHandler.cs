using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class WithdrawPredictionTicketCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, WithdrawPredictionTicketCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, WithdrawPredictionTicketCommand command, CancellationToken cancellationToken)
    {
        aggregate.Withdraw(command.Reason);
        return Task.CompletedTask;
    }
}

using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class DeclarePayoutResultCommandHandler : CommandHandler<RaceAggregate, RaceId, DeclarePayoutResultCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, DeclarePayoutResultCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeclarePayoutResult(command.DeclaredAt, command.WinPayouts, command.PlacePayouts,
            command.QuinellaPayouts, command.ExactaPayouts, command.TrifectaPayouts);
        return Task.CompletedTask;
    }
}

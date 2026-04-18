using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class CloseRaceLifecycleCommandHandler : CommandHandler<RaceAggregate, RaceId, CloseRaceLifecycleCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, CloseRaceLifecycleCommand command, CancellationToken cancellationToken)
    {
        aggregate.CloseRaceLifecycle();
        return Task.CompletedTask;
    }
}

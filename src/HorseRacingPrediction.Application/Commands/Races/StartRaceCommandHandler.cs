using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class StartRaceCommandHandler : CommandHandler<RaceAggregate, RaceId, StartRaceCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, StartRaceCommand command, CancellationToken cancellationToken)
    {
        aggregate.StartRace();
        return Task.CompletedTask;
    }
}

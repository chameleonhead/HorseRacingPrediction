using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class OpenPreRaceCommandHandler : CommandHandler<RaceAggregate, RaceId, OpenPreRaceCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, OpenPreRaceCommand command, CancellationToken cancellationToken)
    {
        aggregate.OpenPreRace();
        return Task.CompletedTask;
    }
}

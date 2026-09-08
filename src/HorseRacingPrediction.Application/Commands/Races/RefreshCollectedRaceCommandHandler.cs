using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RefreshCollectedRaceCommandHandler : CommandHandler<RaceAggregate, RaceId, RefreshCollectedRaceCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RefreshCollectedRaceCommand command, CancellationToken cancellationToken)
    {
        aggregate.RefreshCollectedData(command.Data);
        return Task.CompletedTask;
    }
}

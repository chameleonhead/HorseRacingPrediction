using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class ApplyBulkRaceResultCommandHandler
    : CommandHandler<RaceAggregate, RaceId, ApplyBulkRaceResultCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, ApplyBulkRaceResultCommand command,
        CancellationToken cancellationToken)
    {
        aggregate.ApplyBulkRaceResult(command.Data);
        return Task.CompletedTask;
    }
}

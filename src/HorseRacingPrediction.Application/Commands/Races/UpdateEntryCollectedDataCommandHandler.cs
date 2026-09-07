using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class UpdateEntryCollectedDataCommandHandler
    : CommandHandler<RaceAggregate, RaceId, UpdateEntryCollectedDataCommand>
{
    public override Task ExecuteAsync(
        RaceAggregate aggregate,
        UpdateEntryCollectedDataCommand command,
        CancellationToken cancellationToken)
    {
        aggregate.UpdateEntryCollectedData(
            command.EntryId,
            command.DeclaredWeight,
            command.DeclaredWeightDiff,
            command.OwnerName);
        return Task.CompletedTask;
    }
}

using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class MergeHorseAliasCommandHandler : CommandHandler<HorseAggregate, HorseId, MergeHorseAliasCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, MergeHorseAliasCommand command, CancellationToken cancellationToken)
    {
        aggregate.MergeAlias(command.AliasType, command.AliasValue, command.SourceName, command.IsPrimary);
        return Task.CompletedTask;
    }
}

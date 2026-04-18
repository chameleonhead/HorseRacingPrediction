using EventFlow.Commands;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Commands.Jockeys;

public sealed class MergeJockeyAliasCommandHandler : CommandHandler<JockeyAggregate, JockeyId, MergeJockeyAliasCommand>
{
    public override Task ExecuteAsync(JockeyAggregate aggregate, MergeJockeyAliasCommand command, CancellationToken cancellationToken)
    {
        aggregate.MergeAlias(command.AliasType, command.AliasValue, command.SourceName, command.IsPrimary);
        return Task.CompletedTask;
    }
}

using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class MergeTrainerAliasCommandHandler : CommandHandler<TrainerAggregate, TrainerId, MergeTrainerAliasCommand>
{
    public override Task ExecuteAsync(TrainerAggregate aggregate, MergeTrainerAliasCommand command, CancellationToken cancellationToken)
    {
        aggregate.MergeAlias(command.AliasType, command.AliasValue, command.SourceName, command.IsPrimary);
        return Task.CompletedTask;
    }
}

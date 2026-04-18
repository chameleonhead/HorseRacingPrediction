using EventFlow.Commands;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Application.Commands.Trainers;

public sealed class MergeTrainerAliasCommand : Command<TrainerAggregate, TrainerId>
{
    public MergeTrainerAliasCommand(TrainerId aggregateId, string aliasType, string aliasValue, string sourceName, bool isPrimary)
        : base(aggregateId)
    {
        AliasType = aliasType;
        AliasValue = aliasValue;
        SourceName = sourceName;
        IsPrimary = isPrimary;
    }

    public string AliasType { get; }
    public string AliasValue { get; }
    public string SourceName { get; }
    public bool IsPrimary { get; }
}

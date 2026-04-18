using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Trainers;

public sealed class TrainerAliasMerged : AggregateEvent<TrainerAggregate, TrainerId>
{
    public TrainerAliasMerged(string aliasType, string aliasValue, string sourceName, bool isPrimary)
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

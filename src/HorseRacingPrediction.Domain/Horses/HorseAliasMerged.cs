using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseAliasMerged : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseAliasMerged(string aliasType, string aliasValue, string sourceName, bool isPrimary)
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

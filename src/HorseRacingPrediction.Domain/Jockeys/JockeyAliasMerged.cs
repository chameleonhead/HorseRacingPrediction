using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Jockeys;

public sealed class JockeyAliasMerged : AggregateEvent<JockeyAggregate, JockeyId>
{
    public JockeyAliasMerged(string aliasType, string aliasValue, string sourceName, bool isPrimary)
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

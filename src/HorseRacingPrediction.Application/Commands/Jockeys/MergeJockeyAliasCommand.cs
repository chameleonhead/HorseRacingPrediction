using EventFlow.Commands;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Commands.Jockeys;

public sealed class MergeJockeyAliasCommand : Command<JockeyAggregate, JockeyId>
{
    public MergeJockeyAliasCommand(JockeyId aggregateId, string aliasType, string aliasValue, string sourceName, bool isPrimary)
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

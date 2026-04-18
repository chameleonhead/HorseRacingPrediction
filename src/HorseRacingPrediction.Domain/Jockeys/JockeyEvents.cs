using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Jockeys;

public sealed class JockeyRegistered : AggregateEvent<JockeyAggregate, JockeyId>
{
    public JockeyRegistered(string displayName, string normalizedName, string? affiliationCode = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string DisplayName { get; }
    public string NormalizedName { get; }
    public string? AffiliationCode { get; }
}

public sealed class JockeyProfileUpdated : AggregateEvent<JockeyAggregate, JockeyId>
{
    public JockeyProfileUpdated(string? displayName = null, string? normalizedName = null, string? affiliationCode = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string? DisplayName { get; }
    public string? NormalizedName { get; }
    public string? AffiliationCode { get; }
}

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

public sealed class JockeyDataCorrected : AggregateEvent<JockeyAggregate, JockeyId>
{
    public JockeyDataCorrected(string? displayName = null, string? normalizedName = null,
        string? affiliationCode = null, string? reason = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
        Reason = reason;
    }

    public string? DisplayName { get; }
    public string? NormalizedName { get; }
    public string? AffiliationCode { get; }
    public string? Reason { get; }
}

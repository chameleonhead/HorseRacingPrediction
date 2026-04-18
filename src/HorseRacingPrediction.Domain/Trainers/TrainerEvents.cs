using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Trainers;

public sealed class TrainerRegistered : AggregateEvent<TrainerAggregate, TrainerId>
{
    public TrainerRegistered(string displayName, string normalizedName, string? affiliationCode = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string DisplayName { get; }
    public string NormalizedName { get; }
    public string? AffiliationCode { get; }
}

public sealed class TrainerProfileUpdated : AggregateEvent<TrainerAggregate, TrainerId>
{
    public TrainerProfileUpdated(string? displayName = null, string? normalizedName = null, string? affiliationCode = null)
    {
        DisplayName = displayName;
        NormalizedName = normalizedName;
        AffiliationCode = affiliationCode;
    }

    public string? DisplayName { get; }
    public string? NormalizedName { get; }
    public string? AffiliationCode { get; }
}

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

public sealed class TrainerDataCorrected : AggregateEvent<TrainerAggregate, TrainerId>
{
    public TrainerDataCorrected(string? displayName = null, string? normalizedName = null,
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

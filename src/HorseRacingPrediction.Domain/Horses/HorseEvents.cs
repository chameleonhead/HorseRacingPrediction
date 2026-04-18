using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseRegistered : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseRegistered(string registeredName, string normalizedName,
        string? sexCode = null, DateOnly? birthDate = null)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
    }

    public string RegisteredName { get; }
    public string NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
}

public sealed class HorseProfileUpdated : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseProfileUpdated(string? registeredName = null, string? normalizedName = null,
        string? sexCode = null, DateOnly? birthDate = null)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
    }

    public string? RegisteredName { get; }
    public string? NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
}

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

public sealed class HorseDataCorrected : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseDataCorrected(string? registeredName = null, string? normalizedName = null,
        string? sexCode = null, DateOnly? birthDate = null, string? reason = null)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
        Reason = reason;
    }

    public string? RegisteredName { get; }
    public string? NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
    public string? Reason { get; }
}

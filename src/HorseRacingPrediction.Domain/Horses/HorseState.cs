using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseState : AggregateState<HorseAggregate, HorseId, HorseState>,
    IApply<HorseRegistered>,
    IApply<HorseProfileUpdated>,
    IApply<HorseAliasMerged>,
    IApply<HorseDataCorrected>
{
    private readonly List<AliasDetails> _aliases = new();

    public bool IsRegistered { get; private set; }
    public string? RegisteredName { get; private set; }
    public string? NormalizedName { get; private set; }
    public string? SexCode { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? OwnerName { get; private set; }
    public string? BreederName { get; private set; }
    public string? SireName { get; private set; }
    public string? DamName { get; private set; }
    public IReadOnlyCollection<AliasDetails> Aliases => _aliases.AsReadOnly();

    public void Apply(HorseRegistered e)
    {
        IsRegistered = true;
        RegisteredName = e.RegisteredName;
        NormalizedName = e.NormalizedName;
        SexCode = e.SexCode;
        BirthDate = e.BirthDate;
        OwnerName = e.OwnerName;
        BreederName = e.BreederName; SireName = e.SireName; DamName = e.DamName;
    }

    public void Apply(HorseProfileUpdated e)
    {
        if (e.RegisteredName != null) RegisteredName = e.RegisteredName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.SexCode != null) SexCode = e.SexCode;
        if (e.BirthDate.HasValue) BirthDate = e.BirthDate;
        if (e.OwnerName != null) OwnerName = e.OwnerName;
        if (e.BreederName != null) BreederName = e.BreederName;
        if (e.SireName != null) SireName = e.SireName;
        if (e.DamName != null) DamName = e.DamName;
    }

    public void Apply(HorseAliasMerged e)
    {
        _aliases.Add(new AliasDetails(e.AliasType, e.AliasValue, e.SourceName, e.IsPrimary));
    }

    public void Apply(HorseDataCorrected e)
    {
        if (e.RegisteredName != null) RegisteredName = e.RegisteredName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.SexCode != null) SexCode = e.SexCode;
        if (e.BirthDate.HasValue) BirthDate = e.BirthDate;
    }
}

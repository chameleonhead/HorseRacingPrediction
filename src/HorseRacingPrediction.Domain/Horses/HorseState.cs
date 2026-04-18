using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseState : AggregateState<HorseAggregate, HorseId, HorseState>,
    IApply<HorseRegistered>,
    IApply<HorseProfileUpdated>,
    IApply<HorseAliasMerged>,
    IApply<HorseDataCorrected>,
    IApply<HorseMemoAdded>,
    IApply<HorseMemoUpdated>,
    IApply<HorseMemoDeleted>
{
    private readonly List<AliasDetails> _aliases = new();
    private readonly HashSet<string> _activeMemoIds = new();

    public bool IsRegistered { get; private set; }
    public string? RegisteredName { get; private set; }
    public string? NormalizedName { get; private set; }
    public string? SexCode { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public IReadOnlyCollection<AliasDetails> Aliases => _aliases.AsReadOnly();

    public bool MemoExists(string memoId) => _activeMemoIds.Contains(memoId);

    public void Apply(HorseRegistered e)
    {
        IsRegistered = true;
        RegisteredName = e.RegisteredName;
        NormalizedName = e.NormalizedName;
        SexCode = e.SexCode;
        BirthDate = e.BirthDate;
    }

    public void Apply(HorseProfileUpdated e)
    {
        if (e.RegisteredName != null) RegisteredName = e.RegisteredName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.SexCode != null) SexCode = e.SexCode;
        if (e.BirthDate.HasValue) BirthDate = e.BirthDate;
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

    public void Apply(HorseMemoAdded e) => _activeMemoIds.Add(e.MemoId);

    public void Apply(HorseMemoUpdated e) { }

    public void Apply(HorseMemoDeleted e) => _activeMemoIds.Remove(e.MemoId);
}

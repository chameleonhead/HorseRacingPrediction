using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Trainers;

public sealed class TrainerState : AggregateState<TrainerAggregate, TrainerId, TrainerState>,
    IApply<TrainerRegistered>,
    IApply<TrainerProfileUpdated>,
    IApply<TrainerAliasMerged>,
    IApply<TrainerDataCorrected>
{
    private readonly List<AliasDetails> _aliases = new();

    public bool IsRegistered { get; private set; }
    public string? DisplayName { get; private set; }
    public string? NormalizedName { get; private set; }
    public string? AffiliationCode { get; private set; }
    public IReadOnlyCollection<AliasDetails> Aliases => _aliases.AsReadOnly();

    public void Apply(TrainerRegistered e)
    {
        IsRegistered = true;
        DisplayName = e.DisplayName;
        NormalizedName = e.NormalizedName;
        AffiliationCode = e.AffiliationCode;
    }

    public void Apply(TrainerProfileUpdated e)
    {
        if (e.DisplayName != null) DisplayName = e.DisplayName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.AffiliationCode != null) AffiliationCode = e.AffiliationCode;
    }

    public void Apply(TrainerAliasMerged e)
    {
        _aliases.Add(new AliasDetails(e.AliasType, e.AliasValue, e.SourceName, e.IsPrimary));
    }

    public void Apply(TrainerDataCorrected e)
    {
        if (e.DisplayName != null) DisplayName = e.DisplayName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.AffiliationCode != null) AffiliationCode = e.AffiliationCode;
    }
}

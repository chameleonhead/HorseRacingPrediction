using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Jockeys;

public sealed class JockeyState : AggregateState<JockeyAggregate, JockeyId, JockeyState>,
    IApply<JockeyRegistered>,
    IApply<JockeyProfileUpdated>,
    IApply<JockeyAliasMerged>,
    IApply<JockeyDataCorrected>
{
    private readonly List<AliasDetails> _aliases = new();

    public bool IsRegistered { get; private set; }
    public string? DisplayName { get; private set; }
    public string? NormalizedName { get; private set; }
    public string? AffiliationCode { get; private set; }
    public IReadOnlyCollection<AliasDetails> Aliases => _aliases.AsReadOnly();

    public void Apply(JockeyRegistered e)
    {
        IsRegistered = true;
        DisplayName = e.DisplayName;
        NormalizedName = e.NormalizedName;
        AffiliationCode = e.AffiliationCode;
    }

    public void Apply(JockeyProfileUpdated e)
    {
        if (e.DisplayName != null) DisplayName = e.DisplayName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.AffiliationCode != null) AffiliationCode = e.AffiliationCode;
    }

    public void Apply(JockeyAliasMerged e)
    {
        _aliases.Add(new AliasDetails(e.AliasType, e.AliasValue, e.SourceName, e.IsPrimary));
    }

    public void Apply(JockeyDataCorrected e)
    {
        if (e.DisplayName != null) DisplayName = e.DisplayName;
        if (e.NormalizedName != null) NormalizedName = e.NormalizedName;
        if (e.AffiliationCode != null) AffiliationCode = e.AffiliationCode;
    }
}

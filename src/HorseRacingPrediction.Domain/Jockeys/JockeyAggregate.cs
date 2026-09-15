using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Jockeys;

public class JockeyAggregate : AggregateRoot<JockeyAggregate, JockeyId>,
    IEmit<JockeyRegistered>,
    IEmit<JockeyProfileUpdated>,
    IEmit<JockeyAliasMerged>,
    IEmit<JockeyDataCorrected>
{
    private readonly JockeyState _state = new();

    public JockeyAggregate(JockeyId id)
        : base(id)
    {
        Register(_state);
    }

    public void RegisterJockey(string displayName, string normalizedName, string? affiliationCode = null)
    {
        if (_state.IsRegistered)
            throw new InvalidOperationException("Jockey is already registered.");

        Emit(new JockeyRegistered(displayName, normalizedName, affiliationCode));
    }

    public void UpdateProfile(string? displayName = null, string? normalizedName = null, string? affiliationCode = null)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Jockey is not registered.");

        if ((displayName is null || displayName == _state.DisplayName) &&
            (normalizedName is null || normalizedName == _state.NormalizedName) &&
            (affiliationCode is null || affiliationCode == _state.AffiliationCode))
            return;

        Emit(new JockeyProfileUpdated(displayName, normalizedName, affiliationCode));
    }

    public void UpsertProfile(string? displayName, string? normalizedName, string? affiliationCode = null)
    {
        if (!_state.IsRegistered)
        {
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(normalizedName))
                throw new InvalidOperationException("Jockey name is required for initial upsert.");
            RegisterJockey(displayName, normalizedName, affiliationCode);
            return;
        }

        UpdateProfile(displayName, normalizedName, affiliationCode);
    }

    public void MergeAlias(string aliasType, string aliasValue, string sourceName, bool isPrimary)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Jockey is not registered.");

        Emit(new JockeyAliasMerged(aliasType, aliasValue, sourceName, isPrimary));
    }

    public void CorrectData(string? displayName = null, string? normalizedName = null,
        string? affiliationCode = null, string? reason = null)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Jockey is not registered.");

        if ((displayName is null || displayName == _state.DisplayName) &&
            (normalizedName is null || normalizedName == _state.NormalizedName) &&
            (affiliationCode is null || affiliationCode == _state.AffiliationCode))
            return;

        Emit(new JockeyDataCorrected(displayName, normalizedName, affiliationCode, reason));
    }

    public JockeyDetails GetDetails()
    {
        return new JockeyDetails(
            Id.Value,
            _state.DisplayName,
            _state.NormalizedName,
            _state.AffiliationCode,
            _state.Aliases);
    }

    public void Apply(JockeyRegistered e) { }
    public void Apply(JockeyProfileUpdated e) { }
    public void Apply(JockeyAliasMerged e) { }
    public void Apply(JockeyDataCorrected e) { }
}

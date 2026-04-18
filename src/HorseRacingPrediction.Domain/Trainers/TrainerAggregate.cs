using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Trainers;

public class TrainerAggregate : AggregateRoot<TrainerAggregate, TrainerId>,
    IEmit<TrainerRegistered>,
    IEmit<TrainerProfileUpdated>,
    IEmit<TrainerAliasMerged>,
    IEmit<TrainerDataCorrected>
{
    private readonly TrainerState _state = new();

    public TrainerAggregate(TrainerId id)
        : base(id)
    {
        Register(_state);
    }

    public void RegisterTrainer(string displayName, string normalizedName, string? affiliationCode = null)
    {
        if (_state.IsRegistered)
            throw new InvalidOperationException("Trainer is already registered.");

        Emit(new TrainerRegistered(displayName, normalizedName, affiliationCode));
    }

    public void UpdateProfile(string? displayName = null, string? normalizedName = null, string? affiliationCode = null)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Trainer is not registered.");

        Emit(new TrainerProfileUpdated(displayName, normalizedName, affiliationCode));
    }

    public void MergeAlias(string aliasType, string aliasValue, string sourceName, bool isPrimary)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Trainer is not registered.");

        Emit(new TrainerAliasMerged(aliasType, aliasValue, sourceName, isPrimary));
    }

    public void CorrectData(string? displayName = null, string? normalizedName = null,
        string? affiliationCode = null, string? reason = null)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Trainer is not registered.");

        Emit(new TrainerDataCorrected(displayName, normalizedName, affiliationCode, reason));
    }

    public TrainerDetails GetDetails()
    {
        return new TrainerDetails(
            Id.Value,
            _state.DisplayName,
            _state.NormalizedName,
            _state.AffiliationCode,
            _state.Aliases);
    }

    public void Apply(TrainerRegistered e) { }
    public void Apply(TrainerProfileUpdated e) { }
    public void Apply(TrainerAliasMerged e) { }
    public void Apply(TrainerDataCorrected e) { }
}

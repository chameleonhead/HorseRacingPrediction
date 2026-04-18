using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public class HorseAggregate : AggregateRoot<HorseAggregate, HorseId>,
    IEmit<HorseRegistered>,
    IEmit<HorseProfileUpdated>,
    IEmit<HorseAliasMerged>,
    IEmit<HorseDataCorrected>
{
    private readonly HorseState _state = new();

    public HorseAggregate(HorseId id)
        : base(id)
    {
        Register(_state);
    }

    public void RegisterHorse(string registeredName, string normalizedName,
        string? sexCode = null, DateOnly? birthDate = null)
    {
        if (_state.IsRegistered)
            throw new InvalidOperationException("Horse is already registered.");

        Emit(new HorseRegistered(registeredName, normalizedName, sexCode, birthDate));
    }

    public void UpdateProfile(string? registeredName = null, string? normalizedName = null,
        string? sexCode = null, DateOnly? birthDate = null)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Horse is not registered.");

        Emit(new HorseProfileUpdated(registeredName, normalizedName, sexCode, birthDate));
    }

    public void MergeAlias(string aliasType, string aliasValue, string sourceName, bool isPrimary)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Horse is not registered.");

        Emit(new HorseAliasMerged(aliasType, aliasValue, sourceName, isPrimary));
    }

    public void CorrectData(string? registeredName = null, string? normalizedName = null,
        string? sexCode = null, DateOnly? birthDate = null, string? reason = null)
    {
        if (!_state.IsRegistered)
            throw new InvalidOperationException("Horse is not registered.");

        Emit(new HorseDataCorrected(registeredName, normalizedName, sexCode, birthDate, reason));
    }

    public HorseDetails GetDetails()
    {
        return new HorseDetails(
            Id.Value,
            _state.RegisteredName,
            _state.NormalizedName,
            _state.SexCode,
            _state.BirthDate,
            _state.Aliases);
    }

    public void Apply(HorseRegistered e) { }
    public void Apply(HorseProfileUpdated e) { }
    public void Apply(HorseAliasMerged e) { }
    public void Apply(HorseDataCorrected e) { }
}

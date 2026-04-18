using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public class RaceAggregate : AggregateRoot<RaceAggregate, RaceId>,
    IEmit<RaceCreated>,
    IEmit<RaceCardPublished>,
    IEmit<RaceResultDeclared>
{
    private readonly RaceState _state = new();

    public RaceAggregate(RaceId id)
        : base(id)
    {
        Register(_state);
    }

    public void Create(
        DateOnly raceDate,
        string racecourseCode,
        int raceNumber,
        string raceName)
    {
        if (_state.IsCreated)
        {
            throw new InvalidOperationException("Race is already created.");
        }

        Emit(new RaceCreated(raceDate, racecourseCode, raceNumber, raceName));
    }

    public void PublishCard(int entryCount)
    {
        if (!_state.IsCreated)
        {
            throw new InvalidOperationException("Race is not created.");
        }

        if (_state.Status != RaceStatus.Draft)
        {
            throw new InvalidOperationException("Race card can only be published from Draft state.");
        }

        Emit(new RaceCardPublished(entryCount));
    }

    public void DeclareResult(string winningHorseName, DateTimeOffset declaredAt)
    {
        if (!_state.IsCreated)
        {
            throw new InvalidOperationException("Race is not created.");
        }

        if (_state.Status is not RaceStatus.CardPublished and not RaceStatus.PreRaceOpen and not RaceStatus.InProgress)
        {
            throw new InvalidOperationException("Result can only be declared after card publication.");
        }

        Emit(new RaceResultDeclared(winningHorseName, declaredAt));
    }

    public RaceDetails GetDetails()
    {
        return new RaceDetails(
            Id.Value,
            _state.RaceDate,
            _state.RacecourseCode,
            _state.RaceNumber,
            _state.RaceName,
            _state.Status,
            _state.EntryCount,
            _state.WinningHorseName,
            _state.ResultDeclaredAt);
    }

    public void Apply(RaceCreated aggregateEvent)
    {
    }

    public void Apply(RaceCardPublished aggregateEvent)
    {
    }

    public void Apply(RaceResultDeclared aggregateEvent)
    {
    }
}

public sealed record RaceDetails(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber,
    string? RaceName,
    RaceStatus Status,
    int? EntryCount,
    string? WinningHorseName,
    DateTimeOffset? ResultDeclaredAt);

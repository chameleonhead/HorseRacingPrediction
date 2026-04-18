using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceState : AggregateState<RaceAggregate, RaceId, RaceState>,
    IApply<RaceCreated>,
    IApply<RaceCardPublished>,
    IApply<RaceResultDeclared>
{
    public bool IsCreated { get; private set; }
    public DateOnly? RaceDate { get; private set; }
    public string? RacecourseCode { get; private set; }
    public int? RaceNumber { get; private set; }
    public string? RaceName { get; private set; }
    public RaceStatus Status { get; private set; } = RaceStatus.Draft;
    public int? EntryCount { get; private set; }
    public string? WinningHorseName { get; private set; }
    public DateTimeOffset? ResultDeclaredAt { get; private set; }

    public void Apply(RaceCreated aggregateEvent)
    {
        IsCreated = true;
        RaceDate = aggregateEvent.RaceDate;
        RacecourseCode = aggregateEvent.RacecourseCode;
        RaceNumber = aggregateEvent.RaceNumber;
        RaceName = aggregateEvent.RaceName;
        Status = RaceStatus.Draft;
    }

    public void Apply(RaceCardPublished aggregateEvent)
    {
        EntryCount = aggregateEvent.EntryCount;
        Status = RaceStatus.CardPublished;
    }

    public void Apply(RaceResultDeclared aggregateEvent)
    {
        WinningHorseName = aggregateEvent.WinningHorseName;
        ResultDeclaredAt = aggregateEvent.DeclaredAt;
        Status = RaceStatus.ResultDeclared;
    }
}

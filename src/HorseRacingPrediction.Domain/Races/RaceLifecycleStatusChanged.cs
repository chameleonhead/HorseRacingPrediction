using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceLifecycleStatusChanged : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceLifecycleStatusChanged(RaceStatus newStatus)
    {
        NewStatus = newStatus;
    }

    public RaceStatus NewStatus { get; }
}

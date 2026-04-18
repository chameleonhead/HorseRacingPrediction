using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceTrackConditionObserved : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceTrackConditionObserved(DateTimeOffset observationTime,
        string? turfConditionCode = null, string? dirtConditionCode = null,
        string? goingDescriptionText = null)
    {
        ObservationTime = observationTime;
        TurfConditionCode = turfConditionCode;
        DirtConditionCode = dirtConditionCode;
        GoingDescriptionText = goingDescriptionText;
    }

    public DateTimeOffset ObservationTime { get; }
    public string? TurfConditionCode { get; }
    public string? DirtConditionCode { get; }
    public string? GoingDescriptionText { get; }
}

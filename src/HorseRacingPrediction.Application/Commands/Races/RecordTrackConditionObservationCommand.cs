using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RecordTrackConditionObservationCommand : Command<RaceAggregate, RaceId>
{
    public RecordTrackConditionObservationCommand(RaceId aggregateId, DateTimeOffset observationTime,
        string? turfConditionCode = null, string? dirtConditionCode = null,
        string? goingDescriptionText = null)
        : base(aggregateId)
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

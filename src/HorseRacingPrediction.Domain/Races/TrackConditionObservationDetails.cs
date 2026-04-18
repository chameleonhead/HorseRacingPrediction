namespace HorseRacingPrediction.Domain.Races;

public sealed record TrackConditionObservationDetails(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);

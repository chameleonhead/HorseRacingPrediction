namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record TrackConditionSnapshot(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);

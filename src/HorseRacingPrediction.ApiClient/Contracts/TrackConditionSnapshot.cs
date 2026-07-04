namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed record TrackConditionSnapshot(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);
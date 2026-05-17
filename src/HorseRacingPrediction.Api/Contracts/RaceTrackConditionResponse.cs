namespace HorseRacingPrediction.Api.Contracts;

public sealed record RaceTrackConditionResponse(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);

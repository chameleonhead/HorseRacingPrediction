namespace HorseRacingPrediction.Contracts;

public sealed record RaceTrackConditionDto(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);


namespace HorseRacingPrediction.Contracts.Races;

public sealed record TrackConditionSnapshot(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);

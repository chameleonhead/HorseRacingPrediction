
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceTrackConditionDto(
    DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);

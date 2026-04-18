using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record RecordTrackConditionRequest(
    [property: Required] DateTimeOffset ObservationTime,
    string? TurfConditionCode,
    string? DirtConditionCode,
    string? GoingDescriptionText);

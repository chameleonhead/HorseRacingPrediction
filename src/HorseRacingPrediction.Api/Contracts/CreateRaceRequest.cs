using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record CreateRaceRequest(
    [property: Required] DateOnly RaceDate,
    [property: Required, StringLength(32, MinimumLength = 2)] string RacecourseCode,
    [property: Range(1, 20)] int RaceNumber,
    [property: Required, StringLength(128, MinimumLength = 1)] string RaceName,
    string? RaceId = null);

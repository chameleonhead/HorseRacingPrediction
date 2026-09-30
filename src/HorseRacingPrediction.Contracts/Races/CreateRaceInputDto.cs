using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record CreateRaceInputDto(
    [property: Required] DateOnly RaceDate,
    [property: Required, StringLength(32, MinimumLength = 2)] string RacecourseCode,
    [property: Range(1, 20)] int RaceNumber,
    [property: Required, StringLength(128, MinimumLength = 1)] string RaceName,
    string? RaceId = null,
    string? GradeCode = null,
    string? SurfaceCode = null,
    int? DistanceMeters = null,
    string? DirectionCode = null);

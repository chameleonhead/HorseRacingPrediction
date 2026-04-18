using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record RaceResponse(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber,
    string? RaceName,
    RaceStatus Status,
    int? MeetingNumber,
    int? DayNumber,
    string? GradeCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    int? EntryCount,
    string? WinningHorseName,
    DateTimeOffset? ResultDeclaredAt);

using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record RaceSummaryResponse(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber,
    string? RaceName,
    RaceStatus Status,
    int? EntryCount,
    string? WinningHorseName,
    DateTimeOffset? ResultDeclaredAt);
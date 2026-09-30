
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceSummaryDto(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber,
    string? RaceName,
    RaceStatus Status,
    int? EntryCount,
    string? WinningHorseName,
    DateTimeOffset? ResultDeclaredAt,
    string? ReplacementRaceId = null);

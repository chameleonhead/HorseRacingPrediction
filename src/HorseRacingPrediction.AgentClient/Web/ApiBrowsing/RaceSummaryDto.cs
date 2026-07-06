namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record RaceSummaryDto(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber,
    string? RaceName,
    HorseRacingPrediction.Contracts.RaceStatus Status,
    int? EntryCount,
    string? WinningHorseName,
    DateTimeOffset? ResultDeclaredAt);

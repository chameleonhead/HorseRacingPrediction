namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record JockeyRaceHistoryRowDto(
    string RaceId,
    DateOnly? RaceDate,
    string? RaceName,
    string? RacecourseCode,
    int? RaceNumber,
    string? HorseId,
    string? HorseName,
    int? FinishPosition);

namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record HorseRaceHistoryRowDto(
    string RaceId,
    DateOnly? RaceDate,
    string? RaceName,
    string? RacecourseCode,
    int? RaceNumber,
    int? FinishPosition,
    string? JockeyId,
    string? JockeyName,
    string? TrainerId,
    string? TrainerName);

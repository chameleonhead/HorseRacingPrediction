namespace HorseRacingPrediction.Contracts.Races;

public sealed record CreateRaceFromScheduleInputDto(DateOnly RaceDate, string Course, int RaceNumber, string RaceName);

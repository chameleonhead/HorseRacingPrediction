
namespace HorseRacingPrediction.Contracts.Races;

public sealed record PrepareHorseHistoryRaceRequest(DateOnly RaceDate, string Course, int RaceNumber, string RaceName);

namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceEntryRepairHorseDto(string SourceUrl, int HorseNumber, int GateNumber, string OwnerName);

namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceEntryRepairHorseInputDto(string SourceUrl, int HorseNumber, int GateNumber, string OwnerName);

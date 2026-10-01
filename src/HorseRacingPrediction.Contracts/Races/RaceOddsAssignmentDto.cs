namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceOddsAssignmentDto(int HorseNumber, string HorseId, string EntryId, int? GateNumber);

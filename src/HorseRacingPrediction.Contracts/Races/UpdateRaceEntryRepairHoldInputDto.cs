namespace HorseRacingPrediction.Contracts.Races;

public sealed record UpdateRaceEntryRepairHoldInputDto(string OperationId, long ExpectedGeneration, string Reason);

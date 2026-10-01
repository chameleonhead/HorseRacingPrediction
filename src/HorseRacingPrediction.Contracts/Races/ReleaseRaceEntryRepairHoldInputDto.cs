namespace HorseRacingPrediction.Contracts.Races;

public sealed record ReleaseRaceEntryRepairHoldInputDto(string OperationId, string HoldOperationId, long HoldGeneration,
    int ExpectedVersion, string AssignmentFingerprint, string? RepairOperationId = null,
    string? RepairFingerprint = null, bool CancelRepair = false);

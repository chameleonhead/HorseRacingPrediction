namespace HorseRacingPrediction.Contracts.Races;

public sealed record ApplyRaceEntryRepairResponse(string OperationId, string Fingerprint, int Version, bool Verified,
    string BackupHash, string AssignmentFingerprint);

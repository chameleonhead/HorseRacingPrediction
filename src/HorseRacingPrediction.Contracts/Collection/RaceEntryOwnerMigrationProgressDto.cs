namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RaceEntryOwnerMigrationProgressDto(string BatchId, int Requested, int Corrected,
    int Processing, int Failed, int Unavailable, int Remaining,
    IReadOnlyList<RaceEntryOwnerRepairCandidateDto> Candidates, int Eligible = 0,
    int OutsideCardLookupPeriod = 0);

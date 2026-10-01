namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RaceEntryOwnerRepairCandidateDto(string RaceId, string ResourceId, string? RaceName,
    string RacecourseCode, int RaceNumber, int EntryCount, int MissingOwnerCount, DateOnly Date = default,
    RaceEntryOwnerRepairEligibility Eligibility = RaceEntryOwnerRepairEligibility.CardRetrievalCandidate,
    string? EligibilityReason = null);

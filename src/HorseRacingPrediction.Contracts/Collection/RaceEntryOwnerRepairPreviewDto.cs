namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RaceEntryOwnerRepairPreviewDto(DateOnly Date, int RaceCount,
    IReadOnlyList<RaceEntryOwnerRepairCandidateDto> Candidates);

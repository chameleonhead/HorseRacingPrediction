namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceEntryRepairPreviewDto(bool Eligible, string Fingerprint, int Version,
    IReadOnlyList<RaceRepairEntryDto> PreviousEntries, IReadOnlyList<RaceRepairEntryDto> Entries, string? GradeCode,
    IReadOnlyDictionary<string, int> ReferenceCounts, IReadOnlyList<string> Blockers,
    RaceRepairHoldDto? Hold, RaceEntryRepairManifestDto SourceEvidence, string SourceAssurance);

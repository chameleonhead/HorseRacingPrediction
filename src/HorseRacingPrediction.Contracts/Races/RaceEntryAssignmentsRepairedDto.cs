namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceEntryAssignmentsRepairedDto(string OperationId, string Fingerprint, string SourceUrl,
    DateTimeOffset ObservedAt, string SourceEvidenceJson, IReadOnlyList<RaceRepairEntryDto> PreviousEntries,
    IReadOnlyList<RaceRepairEntryDto> Entries, DateOnly RaceDate, string RacecourseCode, string? GradeCode,
    string? SurfaceCode, int? DistanceMeters, string? DirectionCode);

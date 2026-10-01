namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceEntryRepairManifestDto(int ExpectedVersion, string SourceUrl, DateTimeOffset ObservedAt,
    string? GradeCode, IReadOnlyList<RaceEntryRepairHorseDto> Horses, string SourceSnapshotSha256,
    string? HoldOperationId = null, long HoldGeneration = 0);

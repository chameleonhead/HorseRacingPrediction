namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceEntryRepairManifestInputDto(int ExpectedVersion, string SourceUrl, DateTimeOffset ObservedAt,
    string? GradeCode, IReadOnlyList<RaceEntryRepairHorseInputDto> Horses, string SourceSnapshotSha256,
    string? HoldOperationId = null, long HoldGeneration = 0);

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RaceArtifactSnapshotDto(RaceArtifactKind Artifact, RaceArtifactStatus Status,
    int AppliedRevision, int RequiredRevision, DateTimeOffset? LastObservedAt,
    DateTimeOffset? LastPersistedAt, DateTimeOffset? NextDueAt, string? ErrorCode,
    string? ErrorMessage);

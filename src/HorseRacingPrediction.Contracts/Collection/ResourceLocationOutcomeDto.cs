namespace HorseRacingPrediction.Contracts.Collection;

public sealed record ResourceLocationOutcomeDto(long LocationId, CollectionAttemptResult Result,
    string? ErrorCode = null, RaceArtifactKind? Artifact = null);

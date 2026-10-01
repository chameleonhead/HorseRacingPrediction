namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionStageOutcomeDto(string Stage, RaceArtifactKind Artifact,
    CollectionAttemptResult Result, string? ErrorCode = null, string? ErrorMessage = null,
    Uri? RequestedUrl = null, Uri? FinalUrl = null, bool Persisted = false);

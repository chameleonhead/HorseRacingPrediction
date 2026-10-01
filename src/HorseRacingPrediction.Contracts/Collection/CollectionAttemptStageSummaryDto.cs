namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionAttemptStageSummaryDto(Guid StageOutcomeId, Guid AttemptId, string Stage,
    RaceArtifactKind Artifact, CollectionAttemptResult Result, string? ErrorCode,
    string? ErrorMessage, string? RequestedUrl, string? FinalUrl, bool Persisted);

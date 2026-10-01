namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CompleteCollectionTaskAttemptInputDto(string LeaseToken, CollectionAttemptResult Result,
    string? ErrorCode = null, string? ErrorMessage = null, string? RequestedUrl = null,
    string? FinalUrl = null, int? HttpStatusCode = null, string? PageIdentification = null,
    DateTimeOffset? RetryAt = null, DateTimeOffset? NextCollectionAt = null,
    IReadOnlyList<ResourceLocationOutcomeDto>? LocationOutcomes = null,
    CollectionFailureImpact FailureImpact = CollectionFailureImpact.StopPipeline,
    IReadOnlyList<CollectionStageOutcomeDto>? StageOutcomes = null,
    RaceSchedulingEvidenceDto? RaceEvidence = null);

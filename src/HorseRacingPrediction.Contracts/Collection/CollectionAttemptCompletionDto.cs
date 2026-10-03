namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionAttemptCompletionDto(CollectionAttemptResult Result, string? ErrorCode = null,
    string? ErrorMessage = null, Uri? RequestedUrl = null, Uri? FinalUrl = null,
    int? HttpStatusCode = null, string? PageIdentification = null, DateTimeOffset? RetryAt = null,
    DateTimeOffset? NextCollectionAt = null, IReadOnlyList<ResourceLocationOutcomeDto>? LocationOutcomes = null,
    CollectionFailureImpact FailureImpact = CollectionFailureImpact.StopPipeline,
    IReadOnlyList<CollectionStageOutcomeDto>? StageOutcomes = null,
    RaceSchedulingEvidenceDto? RaceEvidence = null,
    IReadOnlyList<SubjectIdentificationCandidateDto>? IdentificationCandidates = null);

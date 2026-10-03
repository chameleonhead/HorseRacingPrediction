namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionAttemptSummaryDto(Guid AttemptId, Guid TaskId, int AttemptNumber,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, CollectionAttemptResult Result,
    string? ErrorCode, string? ErrorMessage, string? RequestedUrl, string? FinalUrl, int? HttpStatusCode,
    string? PageIdentification = null, Guid? ExecutionBatchId = null, Guid? DispatchEnvelopeId = null,
    string? QueueMessageId = null, string? LambdaRequestId = null, int? BatchTaskOrdinal = null,
    int? BatchTaskCount = null,
    IReadOnlyList<SubjectIdentificationCandidateDto>? IdentificationCandidates = null);

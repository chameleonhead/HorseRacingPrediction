namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionExecutionBatchDetailDto(Guid ExecutionBatchId, Guid DispatchEnvelopeId,
    string QueueMessageId, string? LambdaRequestId, int BatchTaskCount, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, IReadOnlyList<CollectionExecutionBatchTaskSummaryDto> Tasks);

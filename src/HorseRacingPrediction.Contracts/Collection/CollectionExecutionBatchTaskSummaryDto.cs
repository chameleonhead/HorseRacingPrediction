namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionExecutionBatchTaskSummaryDto(Guid TaskId, CollectionResourceKeyDto Resource,
    CollectionDefinitionIdDto Definition, CollectionTaskStatus Status, CollectionAttemptResult Result,
    int AttemptNumber, int BatchTaskOrdinal, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);

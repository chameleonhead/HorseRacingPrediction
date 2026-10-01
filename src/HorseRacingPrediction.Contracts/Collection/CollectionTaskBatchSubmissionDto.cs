namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionTaskBatchSubmissionDto(string Mode, CollectionBulkExecutionDto? PreviewSelection,
    CollectionRequestBulkResponse? ExplicitItems);

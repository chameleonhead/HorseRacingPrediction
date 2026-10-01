namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CreateCollectionTaskBatchInputDto(string Mode,
    CollectionSelectionTaskBatchInputDto? PreviewSelection = null,
    CollectionRequestBulkRequest? ExplicitItems = null);

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionBulkExecutionDto(string BatchId, int TargetCount, int TasksCreated,
    IReadOnlyList<CollectionRequestReceiptDto> Requests);

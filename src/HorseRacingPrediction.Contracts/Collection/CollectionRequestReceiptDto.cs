namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRequestReceiptDto(
    Guid RequestId,
    Guid? TaskId,
    bool CreatedTask,
    bool DeferredByRepairHold = false);

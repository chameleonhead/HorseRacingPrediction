namespace HorseRacingPrediction.Contracts.Collection;

public sealed record AcquireNextExecutionInputDto(CollectionWakeSignalDto Wake, string QueueMessageId);

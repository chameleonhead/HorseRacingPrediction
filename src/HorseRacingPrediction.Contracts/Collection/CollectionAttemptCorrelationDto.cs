namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionAttemptCorrelationDto(Guid ExecutionBatchId, Guid DispatchEnvelopeId,
    string QueueMessageId, string? LambdaRequestId, int BatchTaskOrdinal, int BatchTaskCount)
{
    public bool IsSupported() => ExecutionBatchId != Guid.Empty && DispatchEnvelopeId != Guid.Empty
        && !string.IsNullOrWhiteSpace(QueueMessageId) && QueueMessageId.Length <= 256
        && (LambdaRequestId is null || LambdaRequestId.Length <= 256)
        && BatchTaskCount > 0 && BatchTaskOrdinal > 0 && BatchTaskOrdinal <= BatchTaskCount;
}

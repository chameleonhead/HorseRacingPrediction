using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionExecutionAcquireResultDto(CollectionExecutionAcquireStatus Status,
    Guid? ExecutionBatchId = null, string? LeaseToken = null, CollectionDispatchEnvelopeDto? Envelope = null,
    [property: JsonConverter(typeof(OffsetPreservingDateTimeOffsetJsonConverter))] DateTimeOffset? StartBefore = null,
    CollectionExecutionNoWorkReason? NoWorkReason = null,
    CollectionReservationReleaseOutcome? ReservationReleaseOutcome = null)
{
    public bool SafeToReleaseReservation => Status == CollectionExecutionAcquireStatus.NoWork
        && NoWorkReason is (CollectionExecutionNoWorkReason.PipelinePaused
            or CollectionExecutionNoWorkReason.TaskIneligible
            or CollectionExecutionNoWorkReason.RepairHold
            or CollectionExecutionNoWorkReason.ResourceUnavailable);
}

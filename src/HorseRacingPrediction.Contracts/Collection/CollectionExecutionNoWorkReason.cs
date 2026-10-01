namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionExecutionNoWorkReason
{
    InvalidRequest,
    PipelinePaused,
    LeaseConflict,
    ReservationUnavailable,
    ReservationInconsistent,
    TaskIneligible,
    RepairHold,
    ResourceUnavailable,
    EnvelopeInvalid,
}

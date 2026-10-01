namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionAttemptResult
{
    Running,
    Succeeded,
    TransientFailure,
    PermanentFailure,
    ResourceNotFound,
    ResourceNotYetAvailable,
    ParseFailure,
    ValidationFailure,
    UnexpectedPage,
    AccessLimited,
    Cancelled,
    NotApplicable,
}

namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionReservationReleaseOutcome
{
    NotAttempted,
    Released,
    AlreadyReleasedOrChanged,
    SkippedStaleGeneration,
    SkippedActiveLease,
    SkippedAmbiguousRows,
}

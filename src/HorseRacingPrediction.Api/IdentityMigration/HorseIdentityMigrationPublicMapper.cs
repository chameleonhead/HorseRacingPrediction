using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal static class HorseIdentityMigrationPublicMapper
{
    internal static HorseIdentityMigrationOperationResponse ToPublic(
        HorseIdentityMigrationJournalOperation operation)
        => new(operation.OperationId, Status(operation.Status), Reason(operation.Reason),
            operation.CreatedAt, operation.UpdatedAt,
            operation.Results.Take(128).Select(item => new HorseIdentityMigrationOperationResult(
                Result(item.Code), item.Surface is null ? null : Surface(item.Surface.Value),
                Math.Clamp(item.Count, 0, 1_000_000))).ToArray());

    internal static string Status(HorseIdentityMigrationOperationStatus status) => status switch
    {
        HorseIdentityMigrationOperationStatus.Staged => "AwaitingActions",
        HorseIdentityMigrationOperationStatus.Running => "Running",
        HorseIdentityMigrationOperationStatus.Succeeded => "Succeeded",
        HorseIdentityMigrationOperationStatus.Rejected => "Rejected",
        HorseIdentityMigrationOperationStatus.Failed => "Failed",
        HorseIdentityMigrationOperationStatus.Interrupted => "Interrupted",
        HorseIdentityMigrationOperationStatus.PartialUnverified => "Partial",
        _ => "Unavailable",
    };

    private static string Reason(HorseIdentityMigrationJournalReason reason) => reason switch
    {
        HorseIdentityMigrationJournalReason.None => "None",
        HorseIdentityMigrationJournalReason.CapacityExceeded => "CapacityExceeded",
        HorseIdentityMigrationJournalReason.OwnershipBusy => "OwnershipBusy",
        HorseIdentityMigrationJournalReason.RequestConflict => "RequestConflict",
        HorseIdentityMigrationJournalReason.StaleOperation => "StaleOperation",
        HorseIdentityMigrationJournalReason.UnverifiedPages => "UnverifiedPages",
        HorseIdentityMigrationJournalReason.OperationInterrupted => "OperationInterrupted",
        _ => "Unavailable",
    };

    private static string Result(HorseIdentityMigrationResultCode code) => code switch
    {
        HorseIdentityMigrationResultCode.PlanIncomplete => "PlanIncomplete",
        HorseIdentityMigrationResultCode.DestinationCollision => "DestinationCollision",
        HorseIdentityMigrationResultCode.PageMismatch => "PageMismatch",
        HorseIdentityMigrationResultCode.CapacityExceeded => "CapacityExceeded",
        HorseIdentityMigrationResultCode.OperationInterrupted => "OperationInterrupted",
        _ => "Unavailable",
    };

    private static string Surface(MigrationStoreSurface surface) => surface switch
    {
        MigrationStoreSurface.EventStore => "EventStore",
        MigrationStoreSurface.CollectionPlatform => "CollectionPlatform",
        MigrationStoreSurface.PredictionSchedule => "PredictionSchedule",
        MigrationStoreSurface.LocalCollectionQueue => "LocalCollectionQueue",
        _ => "Unavailable",
    };
}

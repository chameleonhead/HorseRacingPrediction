using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal static class HorseIdentityMigrationPublicMapper
{
    internal static HorseIdentityMigrationOperationResponse ToPublic(
        HorseIdentityMigrationJournalOperation operation)
    {
        var failures = operation.Results.Where(item => IsFailureCode(item.Code)).TakeLast(7).ToArray();
        var visibleResults = operation.Results.Where(item => !IsFailureCode(item.Code))
            .Take(128 - failures.Length).Concat(failures);
        return new(operation.OperationId, Status(operation.Status), Reason(operation.Reason),
            operation.CreatedAt, operation.UpdatedAt,
            visibleResults.Select(item => new HorseIdentityMigrationOperationResult(
                Result(item.Code), item.Surface is null ? null : Surface(item.Surface.Value),
                Math.Clamp(item.Count, 0, 1_000_000))).ToArray());
    }

    private static bool IsFailureCode(HorseIdentityMigrationResultCode code) => code is
        HorseIdentityMigrationResultCode.PreparationFailed or HorseIdentityMigrationResultCode.BackupFailed
        or HorseIdentityMigrationResultCode.InspectionFailed or HorseIdentityMigrationResultCode.ApplyFailed
        or HorseIdentityMigrationResultCode.VerificationFailed or HorseIdentityMigrationResultCode.ChildFailed
        or HorseIdentityMigrationResultCode.StartupFailed;

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
        HorseIdentityMigrationJournalReason.PreparationFailed => "PreparationFailed",
        HorseIdentityMigrationJournalReason.BackupFailed => "BackupFailed",
        HorseIdentityMigrationJournalReason.InspectionFailed => "InspectionFailed",
        HorseIdentityMigrationJournalReason.ApplyFailed => "ApplyFailed",
        HorseIdentityMigrationJournalReason.VerificationFailed => "VerificationFailed",
        HorseIdentityMigrationJournalReason.ChildFailed => "ChildFailed",
        HorseIdentityMigrationJournalReason.StartupFailed => "StartupFailed",
        _ => "Unavailable",
    };

    private static string Result(HorseIdentityMigrationResultCode code) => code switch
    {
        HorseIdentityMigrationResultCode.PlanIncomplete => "PlanIncomplete",
        HorseIdentityMigrationResultCode.DestinationCollision => "DestinationCollision",
        HorseIdentityMigrationResultCode.PageMismatch => "PageMismatch",
        HorseIdentityMigrationResultCode.CapacityExceeded => "CapacityExceeded",
        HorseIdentityMigrationResultCode.OperationInterrupted => "OperationInterrupted",
        HorseIdentityMigrationResultCode.PreparationFailed => "PreparationFailed",
        HorseIdentityMigrationResultCode.BackupFailed => "BackupFailed",
        HorseIdentityMigrationResultCode.InspectionFailed => "InspectionFailed",
        HorseIdentityMigrationResultCode.ApplyFailed => "ApplyFailed",
        HorseIdentityMigrationResultCode.VerificationFailed => "VerificationFailed",
        HorseIdentityMigrationResultCode.ChildFailed => "ChildFailed",
        HorseIdentityMigrationResultCode.StartupFailed => "StartupFailed",
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

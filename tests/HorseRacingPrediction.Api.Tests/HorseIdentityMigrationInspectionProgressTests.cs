using System.Text.Json;
using HorseRacingPrediction.Api.Endpoints.IdentityMigration;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationInspectionProgressTests
{
    [TestMethod]
    public void DeadlinePreviewKeepsOnlyCompletedPageLowerBoundsAndRemainsUnstageable()
    {
        var progress = new HorseIdentityMigrationInspectionProgress();
        progress.StartPhase(HorseIdentityMigrationInspectionPhase.TypedReferences);
        progress.PageCompleted(MigrationStoreSurface.EventStore, "EventEntity", 128, 4096);

        var preview = HorseIdentityMigrationEndpoints.DeadlinePreview(progress);

        Assert.IsFalse(preview.CanStage);
        Assert.IsNull(preview.Fingerprint);
        CollectionAssert.AreEqual(new[] { HorseIdentityMigrationPreviewBlocker.IncompleteEvidence }, preview.Blockers.ToArray());
        Assert.IsNotNull(preview.InspectionDiagnostic);
        Assert.AreEqual(HorseIdentityMigrationInspectionResult.DeadlineExceeded, preview.InspectionDiagnostic.Result);
        Assert.AreEqual(HorseIdentityMigrationInspectionPhase.TypedReferences, preview.InspectionDiagnostic.Phase);
        Assert.AreEqual(HorseIdentityMigrationSchemaSurface.EventStore, preview.InspectionDiagnostic.LastCompletedSurface);
        Assert.AreEqual("EventEntity", preview.InspectionDiagnostic.LastCompletedTable);
        Assert.AreEqual(128, preview.InspectionDiagnostic.InspectedRowsLowerBound);
        Assert.AreEqual(4096, preview.InspectionDiagnostic.InspectedBytesLowerBound);
        Assert.IsGreaterThanOrEqualTo(0, preview.InspectionDiagnostic.ElapsedMilliseconds);
    }

    [TestMethod]
    public void DeadlinePreviewPropagatesCallerCancellationInsteadOfReturningDeadlineMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => HorseIdentityMigrationEndpoints.DeadlinePreview(
            new HorseIdentityMigrationInspectionProgress(), cancellation.Token));
    }

    [TestMethod]
    public void ProgressIsRequestLocalAndUnrecognizedTableNamesAreNotEchoed()
    {
        var first = new HorseIdentityMigrationInspectionProgress();
        var second = new HorseIdentityMigrationInspectionProgress();
        first.StartPhase(HorseIdentityMigrationInspectionPhase.TypedReferences);
        first.PageCompleted(MigrationStoreSurface.CollectionPlatform, "collection_resources", 12, 400);
        second.PageCompleted(MigrationStoreSurface.CollectionPlatform, "untrusted_customer_table", 99, 999);

        var firstSnapshot = first.Snapshot();
        var secondSnapshot = second.Snapshot();

        Assert.AreEqual("collection_resources", firstSnapshot.LastCompletedTable);
        Assert.AreEqual(12, firstSnapshot.InspectedRowsLowerBound);
        Assert.IsNull(secondSnapshot.LastCompletedTable);
        Assert.AreEqual(99, secondSnapshot.InspectedRowsLowerBound);
        Assert.IsFalse(JsonSerializer.Serialize(secondSnapshot).Contains("untrusted_customer_table", StringComparison.Ordinal));
        Assert.AreEqual(HorseIdentityMigrationInspectionPhase.Request, secondSnapshot.Phase,
            "An independent request must retain its own request phase.");
    }

    [TestMethod]
    public void ProgressStartsAtRequestPhaseBeforeStoreInspectionBegins()
    {
        var progress = new HorseIdentityMigrationInspectionProgress();

        Assert.AreEqual(HorseIdentityMigrationInspectionPhase.Request, progress.Snapshot().Phase);
    }

    [TestMethod]
    public void IncompleteFinishPreservesTheActiveInspectionPhase()
    {
        var progress = new HorseIdentityMigrationInspectionProgress();
        progress.StartPhase(HorseIdentityMigrationInspectionPhase.Roots);

        progress.Finish(complete: false);

        var snapshot = progress.Snapshot();
        Assert.AreEqual(HorseIdentityMigrationInspectionPhase.Roots, snapshot.Phase);
        Assert.AreEqual(HorseIdentityMigrationInspectionResult.Incomplete, snapshot.Result);
    }
}

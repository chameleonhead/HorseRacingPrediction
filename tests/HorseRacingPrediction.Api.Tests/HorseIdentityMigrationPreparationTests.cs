using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Api.Endpoints.IdentityMigration;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationPreparationTests
{
    [TestMethod]
    public void PreparationRequiresStablePauseZeroRunningAndFullProductionDuration()
    {
        var pauseStartedAt = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        var stablePause = new CollectionPipelineState(true, "operator", pauseStartedAt);

        Assert.IsFalse(HorseIdentityMigrationPreparationService.IsReadyToStop(stablePause, pauseStartedAt,
            runningCount: 0, pauseStartedAt + TimeSpan.FromSeconds(899)));
        Assert.IsTrue(HorseIdentityMigrationPreparationService.IsReadyToStop(stablePause, pauseStartedAt,
            runningCount: 0, pauseStartedAt + TimeSpan.FromSeconds(900)));
        Assert.IsFalse(HorseIdentityMigrationPreparationService.IsReadyToStop(stablePause, pauseStartedAt,
            runningCount: 1, pauseStartedAt + TimeSpan.FromHours(1)));
        Assert.IsFalse(HorseIdentityMigrationPreparationService.IsReadyToStop(
            stablePause with { UpdatedAt = pauseStartedAt.AddSeconds(1) }, pauseStartedAt,
            runningCount: 0, pauseStartedAt + TimeSpan.FromHours(1)));
        Assert.AreEqual(TimeSpan.FromSeconds(900), HorseIdentityMigrationPreparationService.RequiredPauseDuration);
    }

    [TestMethod]
    public async Task PendingRequestCreatesManifestIdentityIntentWithoutPreviewFingerprint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "horse-migration-preparation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var journal = new HorseIdentityMigrationJournal(directory, []);
            var manifest = new HorseIdentityMigrationManifest(
            [new HorseIdentityMigrationManifestEntry(
                "horse-11111111-1111-4111-8111-111111111111", "jra-horse:2024100828", true)]);
            var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
            var operationId = Guid.NewGuid();
            Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
                requestHash, DateTimeOffset.UtcNow)).Succeeded);
            using var ownership = HorseIdentityMigrationRuntimeOwnership.Acquire([]);
            var bootstrap = HorseIdentityMigrationMaintenanceCapability.CreateApiBootstrapAuthorization(
                HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash([]), 1, 1, new byte[32]);
            using var preparation = new HorseIdentityMigrationPreparationService(journal,
                new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions())), ownership, bootstrap,
                TimeProvider.System, lifetime: null!, NullLogger<HorseIdentityMigrationPreparationService>.Instance);

            var result = await preparation.RequestExecutionAsync(operationId, CancellationToken.None);

            Assert.AreEqual(HorseIdentityMigrationExecutionRequestResult.Accepted, result);
            var intent = await journal.GetExecutionIntentAsync(operationId);
            Assert.IsNotNull(intent);
            Assert.AreEqual(requestHash, intent.ImmutableRequestHash,
                "The manual intent binds the unchanged pending manifest identity; it must not wait on full evidence.");
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.IntentRecorded, intent.Phase);
            var executable = await journal.GetExecutableRequestAsync(operationId);
            Assert.IsNotNull(executable);
            Assert.AreEqual(HorseIdentityMigrationAdmissionKind.PendingV2, executable.RequestKind);
            Assert.IsNull(executable.PreviewFingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PreparationFailurePersistsPublicAndJournalFailureWithoutStoppingHost()
    {
        var directory = Path.Combine(Path.GetTempPath(), "horse-migration-preparation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var journal = new HorseIdentityMigrationJournal(directory, []);
            var manifest = new HorseIdentityMigrationManifest(
            [new HorseIdentityMigrationManifestEntry(
                "horse-11111111-1111-4111-8111-111111111111", "jra-horse:2024100828", true)]);
            var operationId = Guid.NewGuid();
            var requestHash = HorseIdentityMigrationExecutionPolicy.ComputePendingImmutableRequestHash(manifest);
            var failureAt = new DateTimeOffset(2026, 10, 25, 12, 34, 56, TimeSpan.Zero);
            Assert.IsTrue((await journal.StagePendingWithRequestKeyAsync(operationId, manifest, Guid.NewGuid(),
                requestHash, failureAt.AddMinutes(-1))).Succeeded);

            using var ownership = HorseIdentityMigrationRuntimeOwnership.Acquire([]);
            var bootstrap = HorseIdentityMigrationMaintenanceCapability.CreateApiBootstrapAuthorization(
                HorseIdentityMigrationRuntimeOwnership.ComputeSourceSetHash([]), 1, 1, new byte[32]);
            var lifetime = new RecordingHostApplicationLifetime();
            using var preparation = new HorseIdentityMigrationPreparationService(journal,
                new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
                {
                    StateDirectory = Path.Combine(directory, "collection"),
                })), ownership, bootstrap, new FixedTimeProvider(failureAt), lifetime,
                NullLogger<HorseIdentityMigrationPreparationService>.Instance);

            await preparation.StartAsync(CancellationToken.None);
            Assert.AreEqual(HorseIdentityMigrationExecutionRequestResult.Accepted,
                await preparation.RequestExecutionAsync(operationId, CancellationToken.None));

            HorseIdentityMigrationJournalOperation? failed = null;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                failed = await journal.GetStatusAsync(operationId);
                if (failed?.Status == HorseIdentityMigrationOperationStatus.Failed) break;
                await Task.Delay(25);
            }

            Assert.IsNotNull(failed, "Preparation failure was not persisted within the bounded test window.");
            await preparation.StopAsync(CancellationToken.None);
            var reloadedJournal = new HorseIdentityMigrationJournal(directory, []);
            var reloaded = await reloadedJournal.GetStatusAsync(operationId);
            var intent = await reloadedJournal.GetExecutionIntentAsync(operationId);
            var publicResponse = HorseIdentityMigrationEndpoints.ToPublic(reloaded!, saved: null,
                pending: null, intent);

            Assert.AreEqual(HorseIdentityMigrationOperationStatus.Failed, reloaded!.Status);
            Assert.AreEqual(HorseIdentityMigrationJournalReason.PreparationFailed, reloaded.Reason);
            Assert.AreEqual(failureAt, reloaded.UpdatedAt);
            CollectionAssert.AreEqual(new[] { HorseIdentityMigrationResultCode.PreparationFailed },
                reloaded.Results.Select(result => result.Code).ToArray());
            Assert.IsNotNull(intent);
            Assert.AreEqual(HorseIdentityMigrationExecutionPhase.Failed, intent!.Phase);
            Assert.AreEqual(failureAt, intent.UpdatedAt);
            Assert.AreEqual("Failed", publicResponse.Status);
            Assert.AreEqual("PreparationFailed", publicResponse.Reason);
            Assert.AreEqual(failureAt, publicResponse.UpdatedAt);
            Assert.AreEqual("Failed", publicResponse.ExecutionPhase);
            Assert.AreEqual(failureAt, publicResponse.ExecutionUpdatedAt);
            Assert.AreEqual("PreparationFailed", publicResponse.Results.Single().Code);
            Assert.IsFalse(lifetime.StopRequested, "Preparation failure must not stop the host.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingHostApplicationLifetime : IHostApplicationLifetime
    {
        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopRequested = true;
    }
}

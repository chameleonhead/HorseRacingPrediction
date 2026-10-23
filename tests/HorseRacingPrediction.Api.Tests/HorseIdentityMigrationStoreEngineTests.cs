using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.Infrastructure.Persistence.HorseIdentityMigration;
using HorseRacingPrediction.PredictionScheduling;
using EventFlow.Aggregates;
using EventFlow.EventStores;
using EventFlow.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationStoreEngineTests
{
    private const string OldHorseId = "horse-11111111-1111-4111-8111-111111111111";
    private const string JraIdentity = "jra-horse:2024100828";
    private const string SourceProvenV19FairnessSql = """
        CREATE TABLE "collection_dispatcher_fairness_state" (
            "StateId" INTEGER NOT NULL CONSTRAINT "PK_collection_dispatcher_fairness_state" PRIMARY KEY AUTOINCREMENT,
            "ConsecutiveRealtime" INTEGER NOT NULL,
            "LastGrantedLane" TEXT NULL,
            "LastNonRealtimeLane" TEXT NULL,
            "ReservationSequence" INTEGER NOT NULL,
            "ScanAvailableAt" TEXT NULL,
            "ScanOutboxId" TEXT NULL,
            "UpdatedAt" TEXT NOT NULL
        )
        """;
    private static readonly string[] SourceProvenV19FairnessSqlHashes =
    [
        "303A8EE7192FDD808A960678F34489323E41C26CE157A6A83FBC6EC117B634FE",
        "9BA7E868F8D4BE305013559E43E2E3567D8F071F8A47851A03E69E525E6E3C88",
    ];
    private const string SourceProvenV19SchemaSql = """
CREATE TABLE "collection_active_tasks" (
    "ResourcePk" INTEGER NOT NULL,
    "DefinitionId" TEXT NOT NULL,
    "TaskId" TEXT NOT NULL,
    CONSTRAINT "PK_collection_active_tasks" PRIMARY KEY ("ResourcePk", "DefinitionId")
);
CREATE TABLE "collection_attempt_stage_outcomes" (
    "StageOutcomeId" TEXT NOT NULL CONSTRAINT "PK_collection_attempt_stage_outcomes" PRIMARY KEY,
    "AttemptId" TEXT NOT NULL,
    "Stage" TEXT NOT NULL,
    "Artifact" TEXT NOT NULL,
    "Result" TEXT NOT NULL,
    "ErrorCode" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "RequestedUrl" TEXT NULL,
    "FinalUrl" TEXT NULL,
    "Persisted" INTEGER NOT NULL
);
CREATE TABLE "collection_attempts" (
    "AttemptId" TEXT NOT NULL CONSTRAINT "PK_collection_attempts" PRIMARY KEY,
    "TaskId" TEXT NOT NULL,
    "AttemptNumber" INTEGER NOT NULL,
    "StartedAt" TEXT NOT NULL,
    "FinishedAt" TEXT NULL,
    "Result" TEXT NOT NULL,
    "ErrorCode" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "RequestedUrl" TEXT NULL,
    "FinalUrl" TEXT NULL,
    "HttpStatusCode" INTEGER NULL,
    "PageIdentification" TEXT NULL,
    "ExecutionBatchId" TEXT NULL,
    "DispatchEnvelopeId" TEXT NULL,
    "QueueMessageId" TEXT NULL,
    "LambdaRequestId" TEXT NULL,
    "BatchTaskOrdinal" INTEGER NULL,
    "BatchTaskCount" INTEGER NULL
);
CREATE TABLE "collection_backfill_batches" (
    "BatchId" TEXT NOT NULL CONSTRAINT "PK_collection_backfill_batches" PRIMARY KEY,
    "Provider" TEXT NOT NULL,
    "From" TEXT NOT NULL,
    "To" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ExpansionCompletedAt" TEXT NULL
);
CREATE TABLE "collection_definitions" (
    "DefinitionId" TEXT NOT NULL CONSTRAINT "PK_collection_definitions" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "ResourceType" TEXT NOT NULL,
    "CurrentRevision" INTEGER NOT NULL,
    "Enabled" INTEGER NOT NULL
);
CREATE TABLE "collection_dispatcher_fairness_state" (
    "StateId" INTEGER NOT NULL CONSTRAINT "PK_collection_dispatcher_fairness_state" PRIMARY KEY AUTOINCREMENT,
    "ConsecutiveRealtime" INTEGER NOT NULL,
    "LastGrantedLane" TEXT NULL,
    "LastNonRealtimeLane" TEXT NULL,
    "ReservationSequence" INTEGER NOT NULL,
    "ScanAvailableAt" TEXT NULL,
    "ScanOutboxId" TEXT NULL,
    "UpdatedAt" TEXT NOT NULL
);
CREATE TABLE "collection_execution_leases" (
    "ExecutionBatchId" TEXT NOT NULL CONSTRAINT "PK_collection_execution_leases" PRIMARY KEY,
    "DispatchEnvelopeId" TEXT NOT NULL,
    "WakeId" TEXT NOT NULL,
    "ReservationToken" TEXT NOT NULL,
    "LeaseToken" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "LeaseExpiresAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "StartedAt" TEXT NULL,
    "FinishedAt" TEXT NULL,
    "QueueMessageId" TEXT NULL,
    "LambdaRequestId" TEXT NULL
);
CREATE TABLE "collection_failure_notifications" (
    "NotificationId" TEXT NOT NULL CONSTRAINT "PK_collection_failure_notifications" PRIMARY KEY,
    "TaskId" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "ErrorCode" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "FailedAt" TEXT NOT NULL,
    "AvailableAt" TEXT NOT NULL,
    "PublishedAt" TEXT NULL,
    "PublishAttemptCount" INTEGER NOT NULL,
    "LastPublishError" TEXT NULL,
    "ResolutionStatus" TEXT NOT NULL,
    "RecoveryTaskId" TEXT NULL,
    "RecoveryStartedAt" TEXT NULL,
    "ResolvedAt" TEXT NULL
);
CREATE TABLE "collection_platform_controls" (
    "ControlId" TEXT NOT NULL CONSTRAINT "PK_collection_platform_controls" PRIMARY KEY,
    "IsPaused" INTEGER NOT NULL,
    "Reason" TEXT NULL,
    "UpdatedAt" TEXT NOT NULL
);
CREATE TABLE "collection_request_batch_bindings" (
    "BatchItemId" TEXT NOT NULL CONSTRAINT "PK_collection_request_batch_bindings" PRIMARY KEY,
    "PayloadFingerprint" TEXT NOT NULL,
    "RequestId" TEXT NOT NULL,
    "TaskId" TEXT NULL
);
CREATE TABLE "collection_requests" (
    "RequestId" TEXT NOT NULL CONSTRAINT "PK_collection_requests" PRIMARY KEY,
    "ResourcePk" INTEGER NOT NULL,
    "DefinitionId" TEXT NOT NULL,
    "RequestedRevision" INTEGER NOT NULL,
    "Reason" TEXT NOT NULL,
    "Lane" TEXT NOT NULL,
    "Priority" INTEGER NOT NULL,
    "MetadataJson" TEXT NOT NULL,
    "RequestedAt" TEXT NOT NULL,
    "ExplicitUrl" TEXT NULL,
    "BatchId" TEXT NULL,
    "PayloadFingerprint" TEXT NULL,
    "OriginDefinitionId" TEXT NULL,
    "OriginRequestedRevision" INTEGER NULL
);
CREATE TABLE "collection_resource_suppressions" (
    "Type" TEXT NOT NULL,
    "Provider" TEXT NOT NULL,
    "ResourceId" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    "RepairId" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "PK_collection_resource_suppressions" PRIMARY KEY ("Type", "Provider", "ResourceId")
);
CREATE TABLE "collection_resources" (
    "ResourcePk" INTEGER NOT NULL CONSTRAINT "PK_collection_resources" PRIMARY KEY AUTOINCREMENT,
    "Type" TEXT NOT NULL,
    "Provider" TEXT NOT NULL,
    "ResourceId" TEXT NOT NULL,
    "EffectiveDate" TEXT NULL,
    "AttributesJson" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "collection_revision_impacts" (
    "ImpactId" INTEGER NOT NULL CONSTRAINT "PK_collection_revision_impacts" PRIMARY KEY AUTOINCREMENT,
    "DefinitionId" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    "ScopeType" TEXT NOT NULL,
    "ScopePayload" TEXT NOT NULL
);
CREATE TABLE "collection_revisions" (
    "DefinitionId" TEXT NOT NULL,
    "Revision" INTEGER NOT NULL,
    "Description" TEXT NOT NULL,
    "MayRequireRecollection" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "PK_collection_revisions" PRIMARY KEY ("DefinitionId", "Revision")
);
CREATE TABLE collection_schema_history (
    version INTEGER NOT NULL CONSTRAINT PK_collection_schema_history PRIMARY KEY,
    applied_at TEXT NOT NULL
);
CREATE TABLE "collection_states" (
    "ResourcePk" INTEGER NOT NULL,
    "DefinitionId" TEXT NOT NULL,
    "AppliedRevision" INTEGER NOT NULL,
    "RequiredRevision" INTEGER NOT NULL,
    "LastCollectedAt" TEXT NULL,
    "NextCollectionAt" TEXT NULL,
    "Status" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "PK_collection_states" PRIMARY KEY ("ResourcePk", "DefinitionId")
);
CREATE TABLE "collection_task_outbox" (
    "OutboxId" TEXT NOT NULL CONSTRAINT "PK_collection_task_outbox" PRIMARY KEY,
    "TaskId" TEXT NOT NULL,
    "DispatchGeneration" INTEGER NOT NULL,
    "AvailableAt" TEXT NOT NULL,
    "DispatchedAt" TEXT NULL,
    "ReservationToken" TEXT NULL,
    "ReservedUntilUnixMilliseconds" INTEGER NULL,
    "EnvelopeId" TEXT NULL,
    "WakeId" TEXT NULL,
    "QueueMessageId" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "collection_tasks" (
    "TaskId" TEXT NOT NULL CONSTRAINT "PK_collection_tasks" PRIMARY KEY,
    "RequestId" TEXT NOT NULL,
    "ResourcePk" INTEGER NOT NULL,
    "DefinitionId" TEXT NOT NULL,
    "RequestedRevision" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "Lane" TEXT NOT NULL,
    "Priority" INTEGER NOT NULL,
    "AvailableAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "StartedAt" TEXT NULL,
    "FinishedAt" TEXT NULL,
    "LeaseToken" TEXT NULL,
    "LeaseExpiresAt" TEXT NULL,
    "DispatchGeneration" INTEGER NOT NULL,
    "RaceHoldGeneration" INTEGER NOT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "CancellationRequestedAt" TEXT NULL,
    "OriginDefinitionId" TEXT NULL,
    "OriginRequestedRevision" INTEGER NULL,
    "MetadataJson" TEXT NULL
);
CREATE TABLE "race_artifact_states" (
    "ResourcePk" INTEGER NOT NULL,
    "Artifact" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "AppliedRevision" INTEGER NOT NULL,
    "RequiredRevision" INTEGER NOT NULL,
    "LastObservedAt" TEXT NULL,
    "LastPersistedAt" TEXT NULL,
    "NextDueAt" TEXT NULL,
    "LastAttemptId" TEXT NULL,
    "ErrorCode" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "PK_race_artifact_states" PRIMARY KEY ("ResourcePk", "Artifact")
);
CREATE TABLE "race_repair_holds" (
    "RaceId" TEXT NOT NULL,
    "Generation" INTEGER NOT NULL,
    "OperationId" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ReleasedAt" TEXT NULL,
    "AssignmentFingerprint" TEXT NULL,
    "ReleaseOperationId" TEXT NULL,
    CONSTRAINT "PK_race_repair_holds" PRIMARY KEY ("RaceId", "Generation")
);
CREATE TABLE "race_scheduling_evidence" (
    "ResourcePk" INTEGER NOT NULL CONSTRAINT "PK_race_scheduling_evidence" PRIMARY KEY AUTOINCREMENT,
    "OfficialStartAt" TEXT NULL,
    "Provenance" TEXT NULL,
    "VerifiedAt" TEXT NULL,
    "UpdatedAt" TEXT NOT NULL
);
CREATE TABLE "resource_locations" (
    "LocationId" INTEGER NOT NULL CONSTRAINT "PK_resource_locations" PRIMARY KEY AUTOINCREMENT,
    "ResourcePk" INTEGER NOT NULL,
    "DefinitionId" TEXT NOT NULL,
    "Url" TEXT NOT NULL,
    "Source" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "Artifact" TEXT NULL,
    "DiscoveredAt" TEXT NOT NULL,
    "LastVerifiedAt" TEXT NULL,
    "LastFailedAt" TEXT NULL,
    "LastFailureCode" TEXT NULL
);
CREATE UNIQUE INDEX "IX_collection_active_tasks_TaskId" ON "collection_active_tasks" ("TaskId");
CREATE UNIQUE INDEX "IX_collection_attempt_stage_outcomes_AttemptId_Stage" ON "collection_attempt_stage_outcomes" ("AttemptId", "Stage");
CREATE INDEX "IX_collection_attempts_ExecutionBatchId" ON "collection_attempts" ("ExecutionBatchId");
CREATE UNIQUE INDEX "IX_collection_attempts_TaskId_AttemptNumber" ON "collection_attempts" ("TaskId", "AttemptNumber");
CREATE INDEX "IX_collection_backfill_batches_Provider_From_To" ON "collection_backfill_batches" ("Provider", "From", "To");
CREATE UNIQUE INDEX "IX_collection_execution_leases_DispatchEnvelopeId" ON "collection_execution_leases" ("DispatchEnvelopeId");
CREATE INDEX "IX_collection_execution_leases_Status_LeaseExpiresAt" ON "collection_execution_leases" ("Status", "LeaseExpiresAt");
CREATE INDEX "IX_collection_failure_notifications_PublishedAt_AvailableAt" ON "collection_failure_notifications" ("PublishedAt", "AvailableAt");
CREATE INDEX "IX_collection_failure_notifications_RecoveryTaskId" ON "collection_failure_notifications" ("RecoveryTaskId");
CREATE INDEX "IX_collection_failure_notifications_ResolutionStatus_AvailableAt" ON "collection_failure_notifications" ("ResolutionStatus", "AvailableAt");
CREATE INDEX "IX_collection_failure_notifications_TaskId" ON "collection_failure_notifications" ("TaskId");
CREATE INDEX "IX_collection_request_batch_bindings_RequestId" ON "collection_request_batch_bindings" ("RequestId");
CREATE INDEX "IX_collection_requests_ResourcePk_DefinitionId_RequestedAt" ON "collection_requests" ("ResourcePk", "DefinitionId", "RequestedAt");
CREATE INDEX "IX_collection_resource_suppressions_RepairId" ON "collection_resource_suppressions" ("RepairId");
CREATE UNIQUE INDEX "IX_collection_resources_Type_Provider_ResourceId" ON "collection_resources" ("Type", "Provider", "ResourceId");
CREATE INDEX "IX_collection_revision_impacts_DefinitionId_Revision" ON "collection_revision_impacts" ("DefinitionId", "Revision");
CREATE INDEX "IX_collection_states_Status_NextCollectionAt" ON "collection_states" ("Status", "NextCollectionAt");
CREATE INDEX "IX_collection_task_outbox_DispatchedAt_AvailableAt" ON "collection_task_outbox" ("DispatchedAt", "AvailableAt");
CREATE INDEX "IX_collection_task_outbox_DispatchedAt_ReservedUntilUnixMilliseconds" ON "collection_task_outbox" ("DispatchedAt", "ReservedUntilUnixMilliseconds");
CREATE INDEX "IX_collection_tasks_RequestId" ON "collection_tasks" ("RequestId");
CREATE INDEX "IX_collection_tasks_ResourcePk_DefinitionId_RequestedRevision_CreatedAt" ON "collection_tasks" ("ResourcePk", "DefinitionId", "RequestedRevision", "CreatedAt");
CREATE INDEX "IX_collection_tasks_Status_AvailableAt_Lane_Priority" ON "collection_tasks" ("Status", "AvailableAt", "Lane", "Priority");
CREATE INDEX "IX_race_artifact_states_Status_NextDueAt" ON "race_artifact_states" ("Status", "NextDueAt");
CREATE UNIQUE INDEX "IX_race_repair_holds_OperationId" ON "race_repair_holds" ("OperationId");
CREATE UNIQUE INDEX "IX_race_repair_holds_RaceId" ON "race_repair_holds" ("RaceId") WHERE ReleasedAt IS NULL;
CREATE UNIQUE INDEX "IX_resource_locations_ResourcePk_DefinitionId_Url" ON "resource_locations" ("ResourcePk", "DefinitionId", "Url");
""";

    [TestMethod]
    public async Task ValidManifestReadsConfiguredSchemasAndRootsButNeverClaimsWriteReadiness()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var before = await fixture.SourceSnapshotAsync();

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason,
            string.Join(",", result.Stores.Select(store => $"{store.Surface}:{store.Status}:{store.UnknownObjectCount}")));
        Assert.IsFalse(result.CanWrite, "T7a has not inspected all typed references and cannot authorize a writer.");
        Assert.IsFalse(result.Stores.Any(store => store.Status is MigrationStoreReadStatus.Unavailable
            or MigrationStoreReadStatus.UnknownShape or MigrationStoreReadStatus.Unsupported));
        Assert.AreEqual(4, result.Stores.Count);
        Assert.AreEqual(MigrationStoreReadStatus.NotConfigured,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Status);
        Assert.AreEqual(MigrationRootStatus.Present, result.Roots.Single().SourceStatus);
        Assert.IsFalse(result.Roots.Single().HasTargetCollision);
        Assert.AreEqual(before, await fixture.SourceSnapshotAsync(), "The read-only scan must not mutate source stores.");
    }

    [TestMethod]
    public async Task TargetReviewReadsOnlySelectedRootsAndReportsMissingDuplicateAndCollisionWithoutCensusClaims()
    {
        await using (var fixture = await StoreFixture.CreateAsync())
        {
            await fixture.SeedHorseAsync(OldHorseId);
            await fixture.ExecuteAsync(fixture.EventStorePath, """
                INSERT INTO EventEntity
                    (GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                VALUES (999, 'horse-unrelated-future-row', 'FutureAggregate', 1,
                    'unrelated-batch', 'not-json', 'not-json');
                """);
            var before = await fixture.SourceSnapshotAsync();

            var review = await fixture.CreateEngine().InspectTargetReviewAsync(Manifest(), CancellationToken.None);

            Assert.IsTrue(review.CanSavePending, string.Join(',', review.Blockers));
            Assert.AreEqual(HorseIdentityMigrationTargetReviewRootStatus.Present, review.Roots.Single().Status);
            var json = JsonSerializer.Serialize(review, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            StringAssert.Contains(json, "\"roots\"");
            Assert.IsFalse(json.Contains("fingerprint", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("inspectionDiagnostic", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(json.Contains("typedReferenceEvidence", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(before, await fixture.SourceSnapshotAsync(), "Point-target review is read-only.");
        }

        await using (var missing = await StoreFixture.CreateAsync())
        {
            var review = await missing.CreateEngine().InspectTargetReviewAsync(Manifest(), CancellationToken.None);
            Assert.IsFalse(review.CanSavePending);
            Assert.IsTrue(review.Blockers.Contains(HorseIdentityMigrationTargetReviewBlocker.SourceRootMissing));
            Assert.AreEqual(HorseIdentityMigrationTargetReviewRootStatus.Missing, review.Roots.Single().Status);
        }

        await using (var duplicate = await StoreFixture.CreateAsync())
        {
            await duplicate.SeedHorseAsync(OldHorseId);
            await duplicate.ExecuteAsync(duplicate.EventStorePath, """
                DROP INDEX IF EXISTS IX_EventEntity_AggregateId_AggregateSequenceNumber;
                INSERT INTO EventEntity
                    (GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                SELECT 2, AggregateId, AggregateName, AggregateSequenceNumber, 'duplicate-first-event', Data, Metadata
                FROM EventEntity WHERE AggregateId = 'horse-11111111-1111-4111-8111-111111111111';
                """);
            var review = await duplicate.CreateEngine().InspectTargetReviewAsync(Manifest(), CancellationToken.None);
            Assert.IsFalse(review.CanSavePending);
            Assert.IsTrue(review.Blockers.Contains(HorseIdentityMigrationTargetReviewBlocker.DuplicateSource));
            Assert.AreEqual(HorseIdentityMigrationTargetReviewRootStatus.Duplicate, review.Roots.Single().Status);
        }

        await using (var collision = await StoreFixture.CreateAsync())
        {
            await collision.SeedHorseAsync(OldHorseId);
            await collision.SeedHorseAsync(DestinationHorseId());
            var review = await collision.CreateEngine().InspectTargetReviewAsync(Manifest(), CancellationToken.None);
            Assert.IsFalse(review.CanSavePending);
            Assert.IsTrue(review.Blockers.Contains(HorseIdentityMigrationTargetReviewBlocker.DestinationCollision));
            Assert.AreEqual(HorseIdentityMigrationTargetReviewRootStatus.DestinationCollision, review.Roots.Single().Status);
        }
    }

    [TestMethod]
    public async Task TypedClosureEmitsBoundedImmutablePageEvidence()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var before = await fixture.SourceSnapshotAsync();

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.IsFalse(result.CanWrite);
        Assert.IsNotEmpty(result.TypedReferenceEvidence);
        Assert.IsTrue(result.TypedReferenceEvidence.All(page => page.BeforeHash.Length == 64
            && page.ExpectedAfterHash.Length == 64 && page.RowCount >= 0));
        Assert.AreEqual(before, await fixture.SourceSnapshotAsync());
    }

    [TestMethod]
    public async Task MemoLocatorKeyCollisionIsReportedWithSourceAndDestinationRows()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, $"""
            INSERT INTO MemoSubjects (SubjectKey, Memos) VALUES
                ('Horse:{OldHorseId}', '[]'),
                ('Horse:{DestinationHorseId()}', '[]');
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.DestinationCollision, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.DestinationCollision, result.Reason);
        Assert.IsFalse(result.CanWrite);
        Assert.IsTrue(result.TypedReferenceEvidence.Any(page => page.Table == "MemoSubjects"));
    }

    [TestMethod]
    public async Task RaceProjectionUsesExactDtoSerializerAndChangesOnlyTypedHorseValue()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedRacePredictionContextAsync(OldHorseId);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        var page = result.TypedReferenceEvidence.Single(evidence =>
            evidence.Table == "RacePredictionContexts");
        Assert.AreNotEqual(page.ExpectedAfterHash, page.BeforeHash,
            "The DTO serializer must expose the mapped Horse value in expected-after evidence.");
        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status,
            "Missing EventFlow serializer/factory bindings must explicitly prevent completion.");
        Assert.AreEqual("registered-event-codec-unavailable", result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task OddsAndNestedTicketProjectionDtosRebuildOnlyExactDerivedEntryIds()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        const string raceId = "race-typed-reference";
        var oldEntryId = HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
            .BuildRaceEntryId(raceId, OldHorseId);
        var newHorseId = DestinationHorseId();
        var newEntryId = HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
            .BuildRaceEntryId(raceId, newHorseId);
        await fixture.SeedRacePredictionContextAsync(OldHorseId, oldEntryId);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new HorseRacingPrediction.Contracts.Common.Time.JstStoredDateTimeOffsetJsonConverter());
        var odds = JsonSerializer.Serialize(new[]
        {
            new RaceOddsSnapshot(DateTimeOffset.Parse("2026-10-14T00:00:00+09:00"), [], [],
                [new RaceOddsAssignment(1, OldHorseId, oldEntryId, 1)]),
        }, jsonOptions);
        var mark = new PredictionMarkSnapshot(oldEntryId, "A", 1, .9m, "keep user text");
        var marks = JsonSerializer.Serialize(new[] { mark }, jsonOptions);
        var states = JsonSerializer.Serialize(new[]
        {
            new PredictionComparisonTicketState
            {
                PredictionTicketId = "ticket-typed", PredictorType = "fixture", PredictorId = "predictor-typed",
                Status = TicketStatus.Draft, ConfidenceScore = .5m, SummaryComment = "preserve summary",
                PredictedAt = DateTimeOffset.Parse("2026-10-14T00:00:00+09:00"), Marks = [mark],
                Evaluations = [], EvaluationStatus = EvaluationStatus.Ready,
            },
        }, jsonOptions);
        await fixture.ExecuteAsync(fixture.EventStorePath, $$"""
            UPDATE RacePredictionContexts SET OddsSnapshots = '{{odds.Replace("'", "''")}}'
            WHERE RaceId = '{{raceId}}';
            INSERT INTO PredictionTickets
                (PredictionTicketId, RaceId, PredictorType, PredictorId, ConfidenceScore, SummaryComment,
                 PredictedAt, TicketStatus, Marks, Evaluations, EvaluationStatus)
            VALUES ('ticket-typed', '{{raceId}}', 'fixture', 'predictor-typed', 0.5, 'preserve summary',
                    '2026-10-14T00:00:00+09:00', 0, '{{marks.Replace("'", "''")}}', '[]', 0);
            INSERT INTO PredictionComparisons
                (RaceId, RaceName, WinningHorseName, ResultDeclaredAt, TicketStates, EntryIndexes, EntryResults, PayoutResult)
            VALUES ('{{raceId}}', 'preserve race text', NULL, NULL, '{{states.Replace("'", "''")}}',
                    '[]', '[]', NULL);
            """);

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.IsNull(result.TypedReferenceFailure, result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
        foreach (var table in new[] { "RacePredictionContexts", "PredictionTickets", "PredictionComparisons" })
            Assert.IsTrue(result.TypedReferenceEvidence.Any(page => page.Table == table
                && page.ExpectedAfterHash != page.BeforeHash), $"Expected the typed projection in {table} to change.");

        var sourceContextEntry = JsonSerializer.Deserialize<List<RacePredictionContextEntry>>(
            (await fixture.ReadRacePredictionContextAsync(raceId))[1], jsonOptions)!.Single();
        var expectedContextEntry = sourceContextEntry with { HorseId = newHorseId, EntryId = newEntryId };
        Assert.AreEqual(OldHorseId, sourceContextEntry.HorseId, "Read-only planning must not mutate source rows.");
        Assert.AreEqual(newHorseId, expectedContextEntry.HorseId);
        Assert.AreEqual(newEntryId, expectedContextEntry.EntryId);
        var sourceOdds = await fixture.ReadProjectionJsonAsync("RacePredictionContexts", "OddsSnapshots", "RaceId", raceId);
        var oddsSource = JsonSerializer.Deserialize<List<RaceOddsSnapshot>>(sourceOdds, jsonOptions)!.Single();
        var oddsProjection = oddsSource with
        {
            Assignments = oddsSource.Assignments!.Select(assignment =>
            assignment with { HorseId = newHorseId, EntryId = newEntryId }).ToArray()
        };
        Assert.AreEqual(OldHorseId, oddsSource.Assignments!.Single().HorseId);
        Assert.AreEqual(newHorseId, oddsProjection.Assignments!.Single().HorseId);
        Assert.AreEqual(newEntryId, oddsProjection.Assignments.Single().EntryId);
        var sourceTicketMark = JsonSerializer.Deserialize<List<PredictionMarkSnapshot>>(
            await fixture.ReadProjectionJsonAsync("PredictionTickets", "Marks", "PredictionTicketId", "ticket-typed"),
            jsonOptions)!.Single();
        var ticketMark = sourceTicketMark with { EntryId = newEntryId };
        Assert.AreEqual(oldEntryId, sourceTicketMark.EntryId);
        Assert.AreEqual(newEntryId, ticketMark.EntryId);
        Assert.AreEqual("keep user text", ticketMark.Comment);
        var sourceComparisonState = JsonSerializer.Deserialize<List<PredictionComparisonTicketState>>(
            await fixture.ReadProjectionJsonAsync("PredictionComparisons", "TicketStates", "RaceId", raceId),
            jsonOptions)!.Single();
        var expectedComparisonMark = sourceComparisonState.Marks.Single() with { EntryId = newEntryId };
        Assert.AreEqual(oldEntryId, sourceComparisonState.Marks.Single().EntryId);
        Assert.AreEqual(newEntryId, expectedComparisonMark.EntryId);
        Assert.AreEqual("preserve summary", sourceComparisonState.SummaryComment);
    }

    [TestMethod]
    public async Task RegisteredEventFlowCodecAndTypedProjectionProduceExactDerivedEntryEvidence()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var raceId = "race-typed-reference";
        var oldEntryId = HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
            .BuildRaceEntryId(raceId, OldHorseId);
        await fixture.SeedRacePredictionContextAsync(OldHorseId, oldEntryId);
        var stored = await fixture.ReadRacePredictionContextAsync(raceId);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var originalEntries = JsonSerializer.Deserialize<List<
            HorseRacingPrediction.Application.Queries.ReadModels.RacePredictionContextEntry>>(stored[1], options)!;
        var canonicalEntries = JsonSerializer.Serialize(originalEntries, options);
        Assert.IsTrue(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(stored[1]),
            System.Text.Json.Nodes.JsonNode.Parse(canonicalEntries)), $"{stored[1]} != {canonicalEntries}");

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.IsNull(result.TypedReferenceFailure, result.TypedReferenceFailure);
        var page = result.TypedReferenceEvidence.Single(evidence => evidence.Table == "RacePredictionContexts");
        var newHorseId = DestinationHorseId();
        var newEntryId = HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
            .BuildRaceEntryId(raceId, newHorseId);
        var expectedEntries = JsonSerializer.Serialize(originalEntries.Select(entry => entry with
        {
            HorseId = newHorseId,
            EntryId = newEntryId,
        }), options);
        Assert.AreEqual(page.ExpectedAfterHash,
            await fixture.ComputeRacePredictionContextExpectedAfterHashAsync(raceId, expectedEntries),
            "Evidence must hash the exact serialized HorseId and deterministic EntryId result.");
        using (var document = JsonDocument.Parse(expectedEntries))
        {
            var entry = document.RootElement[0];
            Assert.AreEqual(newHorseId, entry.GetProperty("horseId").GetString());
            Assert.AreEqual(newEntryId, entry.GetProperty("entryId").GetString());
        }
        Assert.IsNull(result.TypedReferenceFailure,
            "A real registered EventFlow serializer/factory must accept the canonical Horse event.");
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task TicketCreatedStreamBindsRaceAndHashesExactDerivedMark()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        var ticket = await fixture.SeedSerializedTicketStreamAsync(OldHorseId);
        var before = await fixture.ReadEventRowsAsync();

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.IsNull(result.TypedReferenceFailure, result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
        var serializer = fixture.EventFlowServices.GetRequiredService<IEventJsonSerializer>();
        var factory = fixture.EventFlowServices.GetRequiredService<IDomainEventFactory>();
        var transformer = new TypedHorseIdentityEventTransformer(serializer, factory,
            new Dictionary<string, string> { [OldHorseId] = DestinationHorseId() });
        var context = new TypedHorseIdentityTicketStreamContext(ticket.AggregateId, 1, ticket.RaceId);
        var expectedRows = before.Select(row =>
        {
            var eventContext = row.Record.AggregateName == nameof(PredictionTicketAggregate)
                && row.Record.AggregateSequenceNumber > 1 ? context : null;
            var transformed = transformer.Transform(row.Record, eventContext);
            Assert.IsTrue(transformed.IsAccepted, transformed.Reason.ToString());
            return (row.RowId, row.BatchId, row.Record, transformed.Event!);
        }).ToArray();
        var pages = result.TypedReferenceEvidence.Where(evidence => evidence.Table == "EventEntity").ToArray();
        Assert.AreEqual((long)before.Count, pages.Sum(page => page.RowCount),
            $"failure={result.TypedReferenceFailure}; status={result.Status}; evidence={string.Join(',', result.TypedReferenceEvidence.Select(page => page.Table))}");
        foreach (var page in pages)
        {
            Assert.IsTrue(long.TryParse(page.FirstKey, out var firstRowId));
            Assert.IsTrue(long.TryParse(page.LastKey, out var lastRowId));
            var pageBefore = before.Where(row => row.RowId >= firstRowId && row.RowId <= lastRowId).ToArray();
            var pageExpected = expectedRows.Where(row => row.RowId >= firstRowId && row.RowId <= lastRowId).ToArray();
            Assert.AreEqual(page.RowCount, pageBefore.LongLength);
            Assert.AreEqual(page.BeforeHash,
                ComputeEventEvidenceHash(pageBefore.Select(row =>
                    (row.RowId, row.BatchId, row.Record, row.Record))));
            Assert.AreEqual(page.ExpectedAfterHash, ComputeEventEvidenceHash(pageExpected));
        }

        var mark = expectedRows.Select(row => row.Item4).Single(record =>
            record.AggregateId == ticket.AggregateId && record.AggregateSequenceNumber == 2);
        var markEvent = (PredictionMarkAdded)serializer.Deserialize(mark.Data, mark.Metadata).GetAggregateEvent();
        Assert.AreEqual(HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
            .BuildRaceEntryId(ticket.RaceId, DestinationHorseId()),
            markEvent.EntryId);
        Assert.AreEqual($"comment {OldHorseId}", markEvent.Comment);
    }

    [TestMethod]
    public async Task RegisteredJockeyAndTrainerStreamsAreSupportedAndHashUnchanged()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedSerializedJockeyTrainerEventsAsync();
        var sourceRows = await fixture.ReadEventRowsAsync();

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.IsNull(result.TypedReferenceFailure, result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
        var subjectRows = sourceRows.Where(row => row.Record.AggregateName is nameof(JockeyAggregate)
            or nameof(TrainerAggregate)).ToArray();
        var subjectPages = result.TypedReferenceEvidence.Where(page => page.Table == "EventEntity"
            && long.TryParse(page.FirstKey, out var first) && long.TryParse(page.LastKey, out var last)
            && subjectRows.Any(row => row.RowId >= first && row.RowId <= last)).ToArray();
        Assert.AreEqual((long)subjectRows.Length, subjectPages.Sum(page => page.RowCount));
        Assert.IsTrue(subjectPages.All(page => page.BeforeHash == page.ExpectedAfterHash),
            "Known unaffected Jockey/Trainer records must be accepted and remain byte-stable.");
    }

    [TestMethod]
    public async Task EventEntityEvidenceHashesAllPhysicalColumnsAndDetectsNonIdentityMutations()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedSerializedJockeyTrainerEventsAsync();

        static TypedHorseIdentityPageEvidence PageFor(HorseIdentityStorePlan result, long rowId) =>
            result.TypedReferenceEvidence.Single(page => page.Table == "EventEntity"
                && long.TryParse(page.FirstKey, out var first) && long.TryParse(page.LastKey, out var last)
                && rowId >= first && rowId <= last);

        var first = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);
        Assert.IsNull(first.TypedReferenceFailure, first.TypedReferenceFailure);
        var firstRows = await fixture.ReadEventRowsAsync();
        var jockey = firstRows.Single(row => row.Record.AggregateName == nameof(JockeyAggregate));
        var firstPage = PageFor(first, jockey.RowId);
        Assert.AreEqual(firstPage.BeforeHash, ComputeEventEvidenceHash(
            [(jockey.RowId, jockey.BatchId, jockey.Record, jockey.Record)]));
        Assert.AreEqual(firstPage.ExpectedAfterHash, ComputeEventEvidenceHash(
            [(jockey.RowId, jockey.BatchId, jockey.Record, jockey.Record)]));

        await fixture.ExecuteAsync(fixture.EventStorePath,
            "UPDATE EventEntity SET BatchId = 'batch-id-mutated' WHERE AggregateName = 'JockeyAggregate';");
        var afterBatchMutation = await fixture.CreateEngineWithEventFlow()
            .InspectAsync(Manifest(), CancellationToken.None);
        Assert.IsNull(afterBatchMutation.TypedReferenceFailure, afterBatchMutation.TypedReferenceFailure);
        var batchRows = await fixture.ReadEventRowsAsync();
        var batchJockey = batchRows.Single(row => row.Record.AggregateName == nameof(JockeyAggregate));
        var batchPage = PageFor(afterBatchMutation, batchJockey.RowId);
        Assert.AreNotEqual(firstPage.BeforeHash, batchPage.BeforeHash,
            "BatchId is a physical source column and must affect page evidence.");
        Assert.AreEqual(batchPage.BeforeHash, ComputeEventEvidenceHash(
            [(batchJockey.RowId, batchJockey.BatchId, batchJockey.Record, batchJockey.Record)]));

        await fixture.ExecuteAsync(fixture.EventStorePath,
            "UPDATE EventEntity SET GlobalSequenceNumber = 1001 WHERE AggregateName = 'JockeyAggregate';");
        var afterSequenceMutation = await fixture.CreateEngineWithEventFlow()
            .InspectAsync(Manifest(), CancellationToken.None);
        Assert.IsNull(afterSequenceMutation.TypedReferenceFailure, afterSequenceMutation.TypedReferenceFailure);
        var sequenceRows = await fixture.ReadEventRowsAsync();
        var sequenceJockey = sequenceRows.Single(row => row.Record.AggregateName == nameof(JockeyAggregate));
        var sequencePage = PageFor(afterSequenceMutation, sequenceJockey.RowId);
        Assert.AreNotEqual(batchPage.BeforeHash, sequencePage.BeforeHash,
            "GlobalSequenceNumber must affect evidence even though rowid owns stable membership.");
        Assert.AreEqual(sequencePage.BeforeHash, ComputeEventEvidenceHash(
            [(sequenceJockey.RowId, sequenceJockey.BatchId, sequenceJockey.Record, sequenceJockey.Record)]));
    }

    [TestMethod]
    public async Task LocalQueueAcceptsOnlyActualWakeAndDispatchSerializationsAndHashesFullRows()
    {
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        serializerOptions.Converters.Add(new HorseRacingPrediction.Contracts.Common.Time
            .JstStoredDateTimeOffsetJsonConverter());
        var wakeBody = JsonSerializer.Serialize(new CollectionWakeSignal(
            Guid.NewGuid(), Guid.NewGuid(), "reservation-closure-fixture"), serializerOptions);
        var dispatchBody = JsonSerializer.Serialize(new CollectionDispatchEnvelope(
            Guid.NewGuid(),
            new CollectionDispatchCompatibilityKey("JRA", new CollectionDefinitionId("race-detail"),
                null, CollectionLane.Normal),
            [new CollectionDispatchTaskReference(Guid.NewGuid(), 1)]), serializerOptions);

        foreach (var body in new[] { wakeBody, dispatchBody })
        {
            await using var fixture = await StoreFixture.CreateAsync();
            await fixture.SeedHorseAsync(OldHorseId);
            await fixture.SeedSerializedHorseEventAsync(OldHorseId);
            await fixture.SeedLocalQueueMessageAsync(body);

            var result = await fixture.CreateEngineWithEventFlow("Local")
                .InspectAsync(Manifest(), CancellationToken.None);

            Assert.IsNull(result.TypedReferenceFailure, result.TypedReferenceFailure);
            Assert.IsFalse(result.CanWrite);
            Assert.IsTrue(result.TypedReferenceEvidence.Any(evidence =>
                evidence.Table == "local_collection_messages"),
                $"Expected queue evidence; status={result.Status}, reason={result.Reason}, "
                + $"schemas={string.Join(',', result.Stores.Select(schema => $"{schema.Surface}:{schema.Status}:{schema.TableCount}:{schema.UnknownObjectCount}"))}, "
                + $"pages={string.Join(',', result.TypedReferenceEvidence.Select(evidence => evidence.Table))}");
            var page = result.TypedReferenceEvidence.Single(evidence =>
                evidence.Table == "local_collection_messages");
            Assert.AreEqual(page.ExpectedAfterHash, page.BeforeHash);
            Assert.AreEqual(await fixture.ComputeLocalQueueEvidenceHashAsync(), page.BeforeHash);
        }
    }

    [TestMethod]
    public async Task LocalQueueRejectsUnknownAndAffectedTypedBodiesBeforeUnchangedDisposition()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new HorseRacingPrediction.Contracts.Common.Time.JstStoredDateTimeOffsetJsonConverter());
        var supportedWake = JsonSerializer.Serialize(new CollectionWakeSignal(
            Guid.NewGuid(), Guid.NewGuid(), "reservation-affected-fixture"), options);
        var affectedWake = supportedWake[..^1] + $",\"horseId\":\"{OldHorseId}\"}}";

        foreach (var (body, expectedFailure) in new[]
        {
            ("{\"futureQueueShape\":true}", "queue-body-shape-unsupported"),
            (affectedWake, "queue-reference-requires-generation-recipe"),
        })
        {
            await using var fixture = await StoreFixture.CreateAsync();
            await fixture.SeedHorseAsync(OldHorseId);
            await fixture.SeedSerializedHorseEventAsync(OldHorseId);
            await fixture.SeedLocalQueueMessageAsync(body);

            var result = await fixture.CreateEngineWithEventFlow("Local")
                .InspectAsync(Manifest(), CancellationToken.None);

            Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
            Assert.AreEqual(expectedFailure, result.TypedReferenceFailure);
            Assert.IsFalse(result.CanWrite);
        }
    }

    [TestMethod]
    public async Task LocalQueueEvidenceDetectsChangesToEveryPhysicalNonBodyColumn()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new HorseRacingPrediction.Contracts.Common.Time.JstStoredDateTimeOffsetJsonConverter());
        var firstBody = JsonSerializer.Serialize(new CollectionWakeSignal(
            Guid.NewGuid(), Guid.NewGuid(), "reservation-mutation-fixture"), options);
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedLocalQueueMessageAsync(firstBody);

        var currentHash = await fixture.InspectLocalQueueHashAsync();
        Assert.AreEqual(await fixture.ComputeLocalQueueEvidenceHashAsync(), currentHash.Page.BeforeHash);
        Assert.AreEqual(currentHash.Page.ExpectedAfterHash, currentHash.Page.BeforeHash);
        var mutations = new (string Sql, string Reason)[]
        {
            ("UPDATE local_collection_messages SET message_id = 7;", "message_id"),
            ("UPDATE local_collection_messages SET visible_at = '2026-10-14 13:00:00.000';", "visible_at"),
            ("UPDATE local_collection_messages SET receipt_handle = 'receipt-mutated';", "receipt_handle"),
            ("UPDATE local_collection_messages SET receive_count = 2;", "receive_count"),
            ("UPDATE local_collection_messages SET dead_letter = 1;", "dead_letter"),
            ("UPDATE local_collection_messages SET created_at = '2026-10-14 12:01:00.000';", "created_at"),
        };
        foreach (var (sql, reason) in mutations)
        {
            await fixture.ExecuteAsync(fixture.QueuePath, sql);
            var next = await fixture.InspectLocalQueueHashAsync();
            Assert.AreNotEqual(next.Page.BeforeHash, currentHash.Page.BeforeHash,
                $"A physical queue-row mutation to {reason} must change the full-row digest.");
            Assert.AreEqual(await fixture.ComputeLocalQueueEvidenceHashAsync(), next.Page.BeforeHash);
            Assert.AreEqual(next.Page.ExpectedAfterHash, next.Page.BeforeHash);
            Assert.IsNull(next.Result.TypedReferenceFailure, next.Result.TypedReferenceFailure);
            currentHash = next;
        }
    }

    [TestMethod]
    public async Task AffectedSnapshotBlocksButUnrelatedSnapshotDoesNot()
    {
        await using (var unrelated = await StoreFixture.CreateAsync())
        {
            await unrelated.SeedHorseAsync(OldHorseId);
            await unrelated.SeedSerializedHorseEventAsync(OldHorseId);
            await unrelated.ExecuteAsync(unrelated.EventStorePath, """
                INSERT INTO SnapshotEntity (AggregateId, AggregateName, AggregateSequenceNumber, Data, Metadata)
                VALUES ('jockey-00000000-0000-4000-8000-000000000401', 'JockeyAggregate', 1, '{}', '{}');
                """);

            var result = await unrelated.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

            Assert.IsNull(result.TypedReferenceFailure, result.TypedReferenceFailure);
            Assert.IsFalse(result.CanWrite);
        }

        await using var affected = await StoreFixture.CreateAsync();
        await affected.SeedHorseAsync(OldHorseId);
        await affected.SeedSerializedHorseEventAsync(OldHorseId);
        await affected.ExecuteAsync(affected.EventStorePath, $$"""
            INSERT INTO SnapshotEntity (AggregateId, AggregateName, AggregateSequenceNumber, Data, Metadata)
            VALUES ('{{OldHorseId}}', 'HorseAggregate', 1, '{}', '{}');
            """);

        var affectedResult = await affected.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual("snapshot-recipe-required", affectedResult.TypedReferenceFailure);
        Assert.IsFalse(affectedResult.CanWrite);
    }

    [TestMethod]
    public async Task MemoHorseSubjectChangesWhileTicketMarkManualEntryRemainsByteStable()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedMemoAndTicketProjectionsAsync(OldHorseId);

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        var memo = result.TypedReferenceEvidence.First(evidence => evidence.Table == "MemoSubjects"
            && evidence.BeforeHash != evidence.ExpectedAfterHash);
        var ticket = result.TypedReferenceEvidence.Single(evidence => evidence.Table == "PredictionTickets");
        Assert.AreNotEqual(memo.ExpectedAfterHash, memo.BeforeHash);
        Assert.AreEqual(ticket.ExpectedAfterHash, ticket.BeforeHash,
            "Ticket marks have a manual entry key but no typed Horse reference and must remain unchanged.");
        Assert.IsNull(result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownEventVersionStopsBoundReaderBeforeAnyCompletion()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath,
            "UPDATE EventEntity SET Metadata = replace(Metadata, '\"event_version\":\"1\"', '\"event_version\":\"99\"');");

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual("registered-event-codec-rejected", result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task AggregateSequenceGapStopsBoundReaderBeforeCodecInference()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, $"""
            INSERT INTO EventEntity
                (GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
            SELECT 2, AggregateId, AggregateName, 3, BatchId, Data, Metadata FROM EventEntity;
            """);

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task NullHorseWithDerivedEntryIdStopsTypedProjectionClosure()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedRacePredictionContextAsync(OldHorseId,
            HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
                .BuildRaceEntryId("race-typed-reference", OldHorseId));
        await fixture.ExecuteAsync(fixture.EventStorePath,
            $"UPDATE RacePredictionContexts SET Entries = replace(Entries, '\"horseId\":\"{OldHorseId}\"', '\"horseId\":null');");

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual("null-horse-derived-entry", result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task NullHorseWithUnrelatedEntryTextDoesNotInferDerivedIdentity()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedRacePredictionContextAsync(OldHorseId, $"manual-{OldHorseId}-entry");
        await fixture.ExecuteAsync(fixture.EventStorePath,
            $"UPDATE RacePredictionContexts SET Entries = replace(Entries, '\"horseId\":\"{OldHorseId}\"', '\"horseId\":null');");

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.IsNull(result.TypedReferenceFailure, result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownAffectedProjectionFieldStopsTypedReferenceClosure()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedRacePredictionContextAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath,
            $"UPDATE RacePredictionContexts SET Entries = replace(Entries, '\"horseId\":\"{OldHorseId}\"', '\"horseId\":\"{OldHorseId}\",\"futureHorseIdentity\":\"{OldHorseId}\"');");

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual("read-model-json-transform-recipe-required", result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ExistingCanonicalHorseRootOrStreamIsReportedAsTargetCollision()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var destination = DestinationHorseId();
        await fixture.SeedHorseAsync(destination);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.DestinationCollision, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.DestinationCollision, result.Reason);
        Assert.IsFalse(result.CanWrite);
        var root = result.Roots.Single();
        Assert.AreEqual(MigrationRootStatus.TargetCollision, root.SourceStatus);
        Assert.IsTrue(root.TargetHorseRootExists);
        Assert.IsTrue(root.TargetEventStreamExists);
    }

    [TestMethod]
    public async Task ExistingCanonicalCollectionResourceNaturalKeyBlocksTheMapping()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(DestinationHorseId());

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.DestinationCollision, result.Status);
        var root = result.Roots.Single();
        Assert.IsTrue(root.TargetCollectionResourceExists);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task TargetCollectionResourceWithoutSourceResourceIsNotNaturalKeyCollision()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(DestinationHorseId());

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason);
        Assert.IsTrue(result.Roots.Single().TargetCollectionResourceExists);
        Assert.IsFalse(result.Roots.Single().HasTargetCollision);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ExistingCanonicalFailureHistoryDoesNotBlockAsNaturalKeyCollision()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var target = DestinationHorseId();
        await fixture.ExecuteAsync(fixture.CollectionPath, $"""
            INSERT INTO collection_failure_notifications
                (NotificationId, TaskId, Status, AttemptCount, FailedAt, AvailableAt,
                 PublishAttemptCount, ResolutionStatus, CanonicalResourceType,
                 CanonicalResourceProvider, CanonicalResourceId)
            VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', 'Failed', 1,
                    '2026-10-14 00:00:00', '2026-10-14 00:00:00', 0, 'Open',
                    'Horse', 'JRA', '{target}');
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason);
        Assert.IsTrue(result.Roots.Single().TargetFailureNotificationExists);
        Assert.IsFalse(result.Roots.Single().HasTargetCollision);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task CollectionRoleLedgerTransformsTypedMetadataAndHistoricalReferencesOnly()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(OldHorseId);
        var requestId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await fixture.ExecuteAsync(fixture.CollectionPath, $$"""
            UPDATE collection_resources
            SET AttributesJson = '{"requestedByHorseId":"{{OldHorseId}}","discoveredFromType":"Horse","discoveredFromProvider":"JRA","discoveredFromId":"{{OldHorseId}}","discoveryAncestors":"{{OldHorseId}}|horse-unrelated"}'
            WHERE Type = 'Horse' AND Provider = 'JRA' AND ResourceId = '{{OldHorseId}}';
            INSERT INTO collection_requests
                (RequestId, ResourcePk, DefinitionId, RequestedRevision, Reason, Lane, Priority, MetadataJson, RequestedAt)
            SELECT '{{requestId}}', ResourcePk, 'horse-profile', 1, 'Manual', 'Normal', 10,
                   '{"requestedByHorseId":"{{OldHorseId}}","note":"keep"}', '2026-10-14 00:00:00'
            FROM collection_resources WHERE Type = 'Horse' AND Provider = 'JRA' AND ResourceId = '{{OldHorseId}}';
            INSERT INTO collection_tasks
                (TaskId, RequestId, ResourcePk, DefinitionId, RequestedRevision, Status, Lane, Priority,
                 AvailableAt, CreatedAt, UpdatedAt, DispatchGeneration, RaceHoldGeneration, AttemptCount, MetadataJson)
            SELECT '{{taskId}}', '{{requestId}}', ResourcePk, 'horse-profile', 1, 'Queued', 'Normal', 10,
                   '2026-10-14 00:00:00', '2026-10-14 00:00:00', '2026-10-14 00:00:00', 0, 0, 0,
                   '{"requestedByHorseId":"{{OldHorseId}}","note":"preserve"}'
            FROM collection_resources WHERE Type = 'Horse' AND Provider = 'JRA' AND ResourceId = '{{OldHorseId}}';
            INSERT INTO collection_failure_notifications
                (NotificationId, TaskId, Status, AttemptCount, FailedAt, AvailableAt,
                 PublishAttemptCount, ResolutionStatus, CanonicalResourceType,
                 CanonicalResourceProvider, CanonicalResourceId)
            VALUES ('{{notificationId}}', '{{taskId}}', 'Failed', 1, '2026-10-14 00:00:00',
                    '2026-10-14 00:00:00', 0, 'Open', 'Horse', 'JRA', '{{OldHorseId}}');
            INSERT INTO collection_subject_identification_selections
                (NotificationId, TaskId, SelectedName, SelectedUrl, ResourceType, Provider, ResourceId,
                 DefinitionId, CanonicalTaskId, SelectedAt, CreatedTask, ReusedTask)
            VALUES ('{{notificationId}}', '{{taskId}}', 'Fixture', 'https://example.invalid/horse',
                    'Horse', 'JRA', '{{OldHorseId}}', 'horse-profile', '{{taskId}}',
                    '2026-10-14 00:00:00', 0, 1);
            """);

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        Assert.IsNull(result.TypedReferenceFailure, result.TypedReferenceFailure);
        Assert.IsFalse(result.CanWrite);
        var collectionPages = result.TypedReferenceEvidence.Where(page =>
            page.Surface == MigrationStoreSurface.CollectionPlatform).ToArray();
        Assert.IsTrue(collectionPages.Select(page => page.Table).Distinct(StringComparer.Ordinal).Count() >= 25);
        foreach (var table in new[] { "collection_resources", "collection_requests", "collection_tasks",
                     "collection_failure_notifications", "collection_subject_identification_selections" })
            Assert.IsTrue(collectionPages.Any(page => page.Table == table
                && page.ExpectedAfterHash != page.BeforeHash), $"Expected exact typed changes in {table}.");
        Assert.IsFalse(result.Roots.Single().HasTargetCollision,
            "Historical failure/selection references must not be mistaken for target-key collisions.");
    }

    [TestMethod]
    public async Task CollectionEvidenceUsesBoundedStableRowIdPages()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedSerializedHorseEventAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            WITH RECURSIVE ids(value) AS (
                SELECT 1 UNION ALL SELECT value + 1 FROM ids WHERE value < 130
            )
            INSERT INTO collection_resources (Type, Provider, ResourceId, AttributesJson, CreatedAt)
            SELECT 'Horse', 'Other', 'unrelated-' || value, '{}', '2026-10-14 00:00:00' FROM ids;
            """);

        var result = await fixture.CreateEngineWithEventFlow().InspectAsync(Manifest(), CancellationToken.None);

        var pages = result.TypedReferenceEvidence.Where(page => page.Table == "collection_resources").ToArray();
        Assert.AreEqual(2, pages.Length);
        CollectionAssert.AreEqual(new long[] { 128, 2 }, pages.Select(page => page.RowCount).ToArray());
        Assert.IsTrue(pages.All(page => long.TryParse(page.FirstKey, out _) && long.TryParse(page.LastKey, out _)));
        Assert.IsTrue(long.Parse(pages[0].LastKey!) < long.Parse(pages[1].FirstKey!),
            "Rowid membership must remain stable even if text identity keys change.");
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownTableOrCriticalColumnShapeStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, "CREATE TABLE unexpected_identity_table (id TEXT PRIMARY KEY);");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.IsTrue(result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).UnknownObjectCount > 0);
        Assert.AreEqual(0, result.Roots.Count, "Unknown schema must prevent source/target row interpretation.");
        Assert.IsFalse(result.CanWrite);
        var diagnostics = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        var eventStore = diagnostics.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.EventStore);
        Assert.IsTrue(eventStore.Differences.Any(item =>
            item.Kind == HorseIdentityMigrationSchemaDifferenceKind.UnknownObject));
        Assert.DoesNotContain("unexpected_identity_table", JsonSerializer.Serialize(diagnostics),
            StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task UnknownColumnStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath,
            "ALTER TABLE Horses ADD COLUMN UnexpectedIdentity TEXT NULL;");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
        var diagnostics = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        Assert.IsTrue(diagnostics.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.EventStore).Differences.Any(item =>
                item.Kind == HorseIdentityMigrationSchemaDifferenceKind.ColumnShape));
    }

    [TestMethod]
    public async Task SameTableCountAndValidHistoryStillReportChangedKnownColumnType()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var baseline = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        var baselineCollection = baseline.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.CollectionPlatform);
        Assert.AreEqual(HorseIdentityMigrationSchemaStatus.Observed, baselineCollection.Status,
            JsonSerializer.Serialize(baselineCollection));
        Assert.AreEqual(26, baselineCollection.TableCount);
        Assert.AreEqual(0, baselineCollection.UnknownObjectCount);
        Assert.Contains(25, baselineCollection.MigrationHistoryVersions);

        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP TABLE collection_schema_history;
            CREATE TABLE collection_schema_history (version TEXT NOT NULL PRIMARY KEY, applied_at TEXT NOT NULL);
            INSERT INTO collection_schema_history (version, applied_at)
            VALUES ('25', 'fixture');
            """);

        var plan = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);
        var diagnostics = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        var collection = diagnostics.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.CollectionPlatform);
        var typeDifference = collection.Differences.FirstOrDefault(item =>
            item.Kind == HorseIdentityMigrationSchemaDifferenceKind.ColumnShape
            && item.KnownIdentifier is not null
            && item.Expected?.DeclaredType != item.Observed?.DeclaredType);

        Assert.IsNotNull(typeDifference, JsonSerializer.Serialize(collection));
        Assert.AreEqual("collection_schema_history.version", typeDifference.KnownIdentifier);
        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, plan.Reason);
        Assert.AreEqual(baselineCollection.TableCount, collection.TableCount);
        Assert.AreEqual(0, collection.UnknownObjectCount);
        Assert.Contains(25, collection.MigrationHistoryVersions);
        Assert.AreEqual(HorseIdentityMigrationSchemaDeclaredType.Integer, typeDifference.Expected?.DeclaredType);
        Assert.AreEqual(HorseIdentityMigrationSchemaDeclaredType.Text, typeDifference.Observed?.DeclaredType);
        Assert.IsFalse(plan.CanWrite);
        Assert.IsEmpty(plan.Roots);
    }

    [TestMethod]
    public async Task ChangedSchemaDefaultStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, """
            DROP INDEX IX_OwnerAliasMappings_OwnerId;
            ALTER TABLE OwnerAliasMappings RENAME TO OwnerAliasMappings_original;
            CREATE TABLE OwnerAliasMappings (
                NormalizedAlias TEXT NOT NULL PRIMARY KEY,
                AliasName TEXT NOT NULL,
                OwnerId TEXT NOT NULL,
                Reason TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                IsDisplayName INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IX_OwnerAliasMappings_OwnerId ON OwnerAliasMappings (OwnerId);
            DROP TABLE OwnerAliasMappings_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
        var diagnostics = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        Assert.IsTrue(diagnostics.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.EventStore).Differences.Any(item =>
                item.Kind == HorseIdentityMigrationSchemaDifferenceKind.DefaultMismatch),
            JsonSerializer.Serialize(diagnostics));
    }

    [TestMethod]
    public async Task UnknownIndexStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath,
            "CREATE INDEX unexpected_identity_index ON Horses (NormalizedName);");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
        var diagnostics = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        Assert.IsTrue(diagnostics.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.EventStore).Differences.Any(item =>
                item.Kind == HorseIdentityMigrationSchemaDifferenceKind.IndexShape));
    }

    [TestMethod]
    public async Task SqlitePrefixLookalikeObjectsStopBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, """
            CREATE TABLE sqliteX_identity_table (id TEXT PRIMARY KEY);
            CREATE INDEX sqliteX_identity_index ON sqliteX_identity_table (id);
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ChangedFilteredIndexPredicateStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP INDEX IX_race_repair_holds_RaceId;
            CREATE UNIQUE INDEX IX_race_repair_holds_RaceId
                ON race_repair_holds (RaceId) WHERE ReleasedAt IS NOT NULL;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ChangedIndexOrderingOrCollationStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP INDEX IX_race_repair_holds_OperationId;
            CREATE UNIQUE INDEX IX_race_repair_holds_OperationId
                ON race_repair_holds (OperationId COLLATE NOCASE DESC);
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ChangedKnownCheckConstraintStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP TABLE collection_dispatcher_fairness_state;
            CREATE TABLE collection_dispatcher_fairness_state (
                StateId INTEGER NOT NULL PRIMARY KEY CHECK (StateId = 2),
                ConsecutiveRealtime INTEGER NOT NULL DEFAULT 0,
                LastGrantedLane TEXT NULL,
                LastNonRealtimeLane TEXT NULL,
                ReservationSequence INTEGER NOT NULL DEFAULT 0,
                ScanAvailableAt TEXT NULL,
                ScanOutboxId TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task FreshEfCollectionLayoutWithoutFairnessCheckIsAccepted()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Observed,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.HasCount(1, result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task SourceProvenV19EfFirstHybridPreservesShapeHistoryAndRows()
    {
        var sourceVariants = new[]
        {
            SourceProvenV19FairnessSql.ReplaceLineEndings("\n"),
            SourceProvenV19FairnessSql.ReplaceLineEndings("\r\n"),
        };
        CollectionAssert.AreEquivalent(SourceProvenV19FairnessSqlHashes,
            sourceVariants.Select(sql => Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(sql)))).ToArray());

        foreach (var sourceSql in sourceVariants)
        {
            await using var fixture = await StoreFixture.CreateAsync(createCollectionStore: false);
            await fixture.SeedHorseAsync(OldHorseId);
            await fixture.CreateSourceProvenV19HybridCollectionAsync(sourceSql);

            await fixture.NormalizeCollectionSqliteSidecarsAsync();
            var before = await SnapshotSqliteFilesAsync(fixture.CollectionPath);
            var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

            Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason,
                JsonSerializer.Serialize(result.Stores));
            var collection = result.Stores.Single(store =>
                store.Surface == MigrationStoreSurface.CollectionPlatform);
            Assert.AreEqual(MigrationStoreReadStatus.Observed, collection.Status);
            CollectionAssert.AreEqual(Enumerable.Range(19, 7).ToArray(),
                collection.MigrationHistoryVersions!.ToArray());
            Assert.HasCount(1, result.Roots);
            Assert.IsFalse(result.CanWrite);
            await fixture.NormalizeCollectionSqliteSidecarsAsync();
            CollectionAssert.AreEquivalent(before, await SnapshotSqliteFilesAsync(fixture.CollectionPath),
                "Readonly inspection must not rewrite the source database.");

            await using var connection = new SqliteConnection(
                $"Data Source={fixture.CollectionPath};Pooling=False");
            await connection.OpenAsync();
            await using var fairness = connection.CreateCommand();
            fairness.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'collection_dispatcher_fairness_state';";
            var fairnessSql = Convert.ToString(await fairness.ExecuteScalarAsync());
            Assert.IsTrue(SourceProvenV19FairnessSqlHashes.Contains(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fairnessSql!))),
                StringComparer.OrdinalIgnoreCase));
            await using var sentinel = connection.CreateCommand();
            sentinel.CommandText = "SELECT StateId, ConsecutiveRealtime, ReservationSequence, LastGrantedLane, UpdatedAt FROM collection_dispatcher_fairness_state WHERE StateId = 1;";
            await using var reader = await sentinel.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(1, reader.GetInt32(0));
            Assert.AreEqual(37, reader.GetInt32(1));
            Assert.AreEqual(41, reader.GetInt32(2));
            Assert.AreEqual("Realtime", reader.GetString(3));
            Assert.AreEqual("2026-10-18 00:00:00", reader.GetString(4));
        }
    }

    [TestMethod]
    public async Task SourceProvenV19HybridRejectsCandidateMutationsWithoutRootsOrWrites()
    {
        var mutations = new[]
        {
            """
            PRAGMA writable_schema = ON;
            UPDATE sqlite_master
            SET sql = replace(sql, 'PRIMARY KEY AUTOINCREMENT', 'PRIMARY KEY CHECK (StateId = 1)')
            WHERE type = 'table' AND name = 'collection_dispatcher_fairness_state';
            PRAGMA writable_schema = OFF;
            """,
            """
            CREATE TABLE t9_unexpected_hybrid_object (Id INTEGER NOT NULL);
            """,
            """
            DELETE FROM collection_schema_history;
            INSERT INTO collection_schema_history (version, applied_at) VALUES (19, 'v19');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (21, 'v21');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (22, 'v22');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (23, 'v23');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (24, 'v24');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (25, 'v25');
            """,
            """
            DELETE FROM collection_schema_history;
            INSERT INTO collection_schema_history (version, applied_at) VALUES (19, 'v19');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (20, 'v20');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (21, 'v21');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (22, 'v22');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (23, 'v23');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (24, 'v24');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (25, 'v25');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (26, 'future');
            """,
            """
            PRAGMA writable_schema = ON;
            UPDATE sqlite_master
            SET sql = replace(sql, '"Lane" TEXT NOT NULL', '"Lane" TEXT NOT NULL DEFAULT ''Normal''')
            WHERE type = 'table' AND name = 'collection_requests';
            PRAGMA writable_schema = OFF;
            """,
        };

        foreach (var mutation in mutations)
        {
            await using var fixture = await StoreFixture.CreateAsync(createCollectionStore: false);
            await fixture.SeedHorseAsync(OldHorseId);
            await fixture.CreateSourceProvenV19HybridCollectionAsync(
                SourceProvenV19FairnessSql.ReplaceLineEndings("\n"));
            await fixture.ExecuteAsync(fixture.CollectionPath, mutation);
            await fixture.NormalizeCollectionSqliteSidecarsAsync();
            var before = await SnapshotSqliteFilesAsync(fixture.CollectionPath);

            var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);
            var collection = result.Stores.Single(store =>
                store.Surface == MigrationStoreSurface.CollectionPlatform);

            Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
            Assert.AreEqual(MigrationStoreReadStatus.UnknownShape, collection.Status);
            Assert.IsEmpty(result.Roots);
            Assert.IsFalse(result.CanWrite);
            Assert.IsFalse(JsonSerializer.Serialize(collection.Differences).Contains(
                "CREATE TABLE", StringComparison.Ordinal));
            await fixture.NormalizeCollectionSqliteSidecarsAsync();
            CollectionAssert.AreEquivalent(before, await SnapshotSqliteFilesAsync(fixture.CollectionPath));
        }
    }

    [TestMethod]
    public async Task AddedCheckConstraintStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP TABLE collection_dispatcher_fairness_state;
            CREATE TABLE collection_dispatcher_fairness_state (
                StateId INTEGER NOT NULL PRIMARY KEY CHECK (StateId = 1),
                ConsecutiveRealtime INTEGER NOT NULL DEFAULT 0,
                LastGrantedLane TEXT NULL,
                LastNonRealtimeLane TEXT NULL,
                ReservationSequence INTEGER NOT NULL DEFAULT 0,
                ScanAvailableAt TEXT NULL,
                ScanOutboxId TEXT NULL,
                UpdatedAt TEXT NOT NULL,
                CHECK (StateId > 0)
            );
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task GeneratedColumnStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath,
            "ALTER TABLE collection_dispatcher_fairness_state ADD COLUMN GeneratedState TEXT GENERATED ALWAYS AS (StateId) VIRTUAL;");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task DeferrableForeignKeyStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP INDEX IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId;
            ALTER TABLE collection_batch_recovery_progress RENAME TO collection_batch_recovery_progress_original;
            CREATE TABLE collection_batch_recovery_progress (
                BatchId TEXT NOT NULL CONSTRAINT PK_collection_batch_recovery_progress PRIMARY KEY
                    REFERENCES collection_backfill_batches (BatchId) ON DELETE CASCADE DEFERRABLE INITIALLY DEFERRED,
                Kind TEXT NOT NULL,
                NextDate TEXT NULL,
                LastVisitedSequence INTEGER NOT NULL,
                ReviewReason TEXT NULL,
                LastErrorCode TEXT NULL
            );
            CREATE INDEX IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId
                ON collection_batch_recovery_progress (Kind, LastVisitedSequence, BatchId);
            DROP TABLE collection_batch_recovery_progress_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
        var diagnostics = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        Assert.IsTrue(diagnostics.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.CollectionPlatform).Differences.Any(item =>
                item.Kind == HorseIdentityMigrationSchemaDifferenceKind.SqlShapeUnsupported));
    }

    [TestMethod]
    public async Task InlineUniqueConstraintStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP TABLE collection_dispatcher_fairness_state;
            CREATE TABLE collection_dispatcher_fairness_state (
                StateId INTEGER NOT NULL PRIMARY KEY CHECK (StateId = 1),
                ConsecutiveRealtime INTEGER NOT NULL DEFAULT 0,
                LastGrantedLane TEXT NULL UNIQUE,
                LastNonRealtimeLane TEXT NULL,
                ReservationSequence INTEGER NOT NULL DEFAULT 0,
                ScanAvailableAt TEXT NULL,
                ScanOutboxId TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task PrimaryKeyDirectionStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP TABLE collection_dispatcher_fairness_state;
            CREATE TABLE collection_dispatcher_fairness_state (
                StateId INTEGER NOT NULL PRIMARY KEY DESC CHECK (StateId = 1),
                ConsecutiveRealtime INTEGER NOT NULL DEFAULT 0,
                LastGrantedLane TEXT NULL,
                LastNonRealtimeLane TEXT NULL,
                ReservationSequence INTEGER NOT NULL DEFAULT 0,
                ScanAvailableAt TEXT NULL,
                ScanOutboxId TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task NullableTextPrimaryKeyStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, """
            ALTER TABLE Horses RENAME TO Horses_original;
            CREATE TABLE Horses (
                HorseId TEXT PRIMARY KEY,
                RegisteredName TEXT NOT NULL,
                NormalizedName TEXT NOT NULL,
                SexCode TEXT NULL,
                BirthDate TEXT NULL,
                OwnerName TEXT NULL,
                BreederName TEXT NULL,
                SireName TEXT NULL,
                DamName TEXT NULL,
                DamsireName TEXT NULL,
                CoatColor TEXT NULL,
                Aliases TEXT NOT NULL
            );
            DROP TABLE Horses_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownCollectionHistoryVersionStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath,
            "INSERT INTO collection_schema_history (version, applied_at) VALUES (26, 'future');");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
        var diagnostics = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        var collection = diagnostics.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.CollectionPlatform);
        Assert.AreEqual(26, collection.TableCount);
        Assert.AreEqual(0, collection.UnknownObjectCount);
        CollectionAssert.Contains(collection.MigrationHistoryVersions.ToArray(), 26);
        Assert.IsTrue(collection.Differences.Any(item =>
            item.Kind == HorseIdentityMigrationSchemaDifferenceKind.MigrationHistory));
    }

    [TestMethod]
    public async Task MissingCollectionHistoryStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath,
            "DELETE FROM collection_schema_history;");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task SourceProvenCollectionUpgradeSuffixThroughVersion25IsAccepted()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DELETE FROM collection_schema_history;
            INSERT INTO collection_schema_history (version, applied_at) VALUES (23, 'v23-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (24, 'v24-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (25, 'v25-fixture');
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Observed,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.HasCount(1, result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task SourceProvenCollectionUpgradeSuffixStartingAtVersion6IsAccepted()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DELETE FROM collection_schema_history;
            INSERT INTO collection_schema_history (version, applied_at) VALUES (6, 'v6-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (7, 'v7-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (8, 'v8-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (9, 'v9-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (10, 'v10-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (11, 'v11-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (12, 'v12-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (13, 'v13-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (14, 'v14-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (15, 'v15-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (16, 'v16-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (17, 'v17-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (18, 'v18-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (19, 'v19-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (20, 'v20-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (21, 'v21-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (22, 'v22-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (23, 'v23-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (24, 'v24-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (25, 'v25-fixture');
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Observed,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.HasCount(1, result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task GappedCollectionUpgradeHistoryStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DELETE FROM collection_schema_history;
            INSERT INTO collection_schema_history (version, applied_at) VALUES (6, 'v6-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (8, 'v8-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (9, 'v9-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (10, 'v10-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (11, 'v11-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (12, 'v12-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (13, 'v13-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (14, 'v14-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (15, 'v15-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (16, 'v16-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (17, 'v17-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (18, 'v18-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (19, 'v19-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (20, 'v20-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (21, 'v21-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (22, 'v22-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (23, 'v23-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (24, 'v24-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (25, 'v25-fixture');
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ChangedCollectionForeignKeyActionStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP INDEX IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId;
            ALTER TABLE collection_batch_recovery_progress RENAME TO collection_batch_recovery_progress_original;
            CREATE TABLE collection_batch_recovery_progress (
                BatchId TEXT NOT NULL CONSTRAINT PK_collection_batch_recovery_progress PRIMARY KEY
                    REFERENCES collection_backfill_batches (BatchId) ON DELETE RESTRICT,
                Kind TEXT NOT NULL,
                NextDate TEXT NULL,
                LastVisitedSequence INTEGER NOT NULL,
                ReviewReason TEXT NULL,
                LastErrorCode TEXT NULL
            );
            CREATE INDEX IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId
                ON collection_batch_recovery_progress (Kind, LastVisitedSequence, BatchId);
            DROP TABLE collection_batch_recovery_progress_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
        var diagnostics = await fixture.CreateEngine().InspectSchemaDiagnosticsAsync(CancellationToken.None);
        Assert.IsTrue(diagnostics.Surfaces.Single(surface =>
            surface.Surface == HorseIdentityMigrationSchemaSurface.CollectionPlatform).Differences.Any(item =>
                item.Kind == HorseIdentityMigrationSchemaDifferenceKind.ForeignKeyMismatch));
    }

    [TestMethod]
    public async Task ChangedCollectionForeignKeyUpdateActionStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP INDEX IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId;
            ALTER TABLE collection_batch_recovery_progress RENAME TO collection_batch_recovery_progress_original;
            CREATE TABLE collection_batch_recovery_progress (
                BatchId TEXT NOT NULL CONSTRAINT PK_collection_batch_recovery_progress PRIMARY KEY
                    REFERENCES collection_backfill_batches (BatchId) ON DELETE CASCADE ON UPDATE CASCADE,
                Kind TEXT NOT NULL,
                NextDate TEXT NULL,
                LastVisitedSequence INTEGER NOT NULL,
                ReviewReason TEXT NULL,
                LastErrorCode TEXT NULL
            );
            CREATE INDEX IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId
                ON collection_batch_recovery_progress (Kind, LastVisitedSequence, BatchId);
            DROP TABLE collection_batch_recovery_progress_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task MissingCollectionForeignKeyStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP INDEX IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId;
            ALTER TABLE collection_batch_recovery_progress RENAME TO collection_batch_recovery_progress_original;
            CREATE TABLE collection_batch_recovery_progress (
                BatchId TEXT NOT NULL CONSTRAINT PK_collection_batch_recovery_progress PRIMARY KEY,
                Kind TEXT NOT NULL,
                NextDate TEXT NULL,
                LastVisitedSequence INTEGER NOT NULL,
                ReviewReason TEXT NULL,
                LastErrorCode TEXT NULL
            );
            CREATE INDEX IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId
                ON collection_batch_recovery_progress (Kind, LastVisitedSequence, BatchId);
            DROP TABLE collection_batch_recovery_progress_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownEventStoreMigrationIdStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, """
            INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
            VALUES ('99999999999999_FutureMigration', '10.0.0');
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task MissingEventStoreMigrationIdStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, """
            DELETE FROM __EFMigrationsHistory
            WHERE MigrationId = (SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId LIMIT 1);
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownScheduleColumnTypeStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.SchedulePath, """
            DROP INDEX IX_prediction_candidates_Status_AvailableAt;
            ALTER TABLE prediction_candidates RENAME TO prediction_candidates_original;
            CREATE TABLE prediction_candidates (
                RaceId TEXT NOT NULL PRIMARY KEY,
                Status TEXT NOT NULL,
                FirstQueuedAt TEXT NOT NULL,
                AvailableAt TEXT NOT NULL,
                LeaseToken TEXT NULL,
                LeaseExpiresAt TEXT NULL,
                LastError TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IX_prediction_candidates_Status_AvailableAt
                ON prediction_candidates (Status, AvailableAt);
            DROP TABLE prediction_candidates_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.PredictionSchedule).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task MissingRequiredDatabaseIsNotCreatedAndOptionalQueueRemainsDistinct()
    {
        await using var fixture = await StoreFixture.CreateAsync(createEventStore: false);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreUnavailable, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Unavailable,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsFalse(File.Exists(fixture.EventStorePath), "Read-only inspection must not create a missing source file.");
        Assert.AreEqual(MigrationStoreReadStatus.NotConfigured,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Status);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownQueueProviderIsUnavailableAndDoesNotCreateAFile()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);

        var result = await fixture.CreateEngine("UnexpectedProvider").InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreUnavailable, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Unavailable,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Status);
        Assert.IsFalse(File.Exists(fixture.QueuePath));
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ConfiguredLocalQueueMissingFileIsUnavailableAndNotCreated()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);

        var result = await fixture.CreateEngine("Local").InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreUnavailable, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Unavailable,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Status);
        Assert.IsFalse(File.Exists(fixture.QueuePath));
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task CancellationStopsInspectionBeforeAnyRootRead()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.CreateEngine().InspectAsync(Manifest(), cancellation.Token));
    }

    [TestMethod]
    public async Task ReadOnlyConnectionRejectsWritesAndLeavesSourceBytesUnchanged()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var before = await File.ReadAllBytesAsync(fixture.EventStorePath);

        await using var connection = await TypedHorseIdentityStoreReaders.OpenReadOnlyAsync(
            fixture.EventStorePath, CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Horses (HorseId, RegisteredName, NormalizedName, Aliases) VALUES ('horse-22222222-2222-4222-8222-222222222222', 'x', 'x', '[]');";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        var after = await File.ReadAllBytesAsync(fixture.EventStorePath);
        CollectionAssert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task InvalidManifestDoesNotOpenOrInspectAnyStore()
    {
        await using var fixture = await StoreFixture.CreateAsync(createEventStore: false);
        var manifest = new HorseIdentityMigrationManifest(
        [
            new(OldHorseId, JraIdentity, HypothesisAccepted: false),
        ]);

        var result = await fixture.CreateEngine().InspectAsync(manifest, CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.InvalidManifest, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.InvalidManifest, result.Reason);
        Assert.IsTrue(result.ManifestErrorCount > 0);
        Assert.AreEqual(0, result.Stores.Count);
        Assert.IsFalse(File.Exists(fixture.EventStorePath));
    }

    [TestMethod]
    public async Task HistoricalV6FixtureProvesSourceUpgradeAndRemainsFailClosed()
        => await AssertHistoricalFixtureAsync("v6-e170c4b3", expectedStartVersion: 6,
            expectsRaceRepairHold: false);

    [TestMethod]
    public async Task HistoricalV18FixtureProvesSourceUpgradeAndRemainsFailClosed()
        => await AssertHistoricalFixtureAsync("v18-8bbee4b0", expectedStartVersion: 18,
            expectsRaceRepairHold: true);

    [TestMethod]
    public async Task HistoricalV18FixtureWithoutManualCheckStopsBeforeRootReads()
        => await AssertHistoricalFixtureAsync("v18-8bbee4b0", expectedStartVersion: 18,
            expectsRaceRepairHold: true, expectSchemaRejection: true, mutationSql: """
            PRAGMA writable_schema = ON;
            UPDATE sqlite_master
            SET sql = replace(sql, ' CHECK (StateId = 1)', '')
            WHERE type = 'table' AND name = 'collection_dispatcher_fairness_state';
            PRAGMA writable_schema = OFF;
            """);

    [TestMethod]
    public async Task HistoricalV18FixtureWithoutManualCheckCannotBypassWithGenericHistory()
        => await AssertHistoricalFixtureAsync("v18-8bbee4b0", expectedStartVersion: 18,
            expectsRaceRepairHold: true, expectSchemaRejection: true, mutationSql: """
            DELETE FROM collection_schema_history;
            INSERT INTO collection_schema_history (version, applied_at) VALUES (17, 'v17-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (18, 'v18-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (19, 'v19-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (20, 'v20-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (21, 'v21-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (22, 'v22-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (23, 'v23-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (24, 'v24-fixture');
            INSERT INTO collection_schema_history (version, applied_at) VALUES (25, 'v25-fixture');
            PRAGMA writable_schema = ON;
            UPDATE sqlite_master
            SET sql = replace(sql, ' CHECK (StateId = 1)', '')
            WHERE type = 'table' AND name = 'collection_dispatcher_fairness_state';
            PRAGMA writable_schema = OFF;
            """);

    [TestMethod]
    public async Task HistoricalV6FixtureWithChangedManualDefaultStopsBeforeRootReads()
        => await AssertHistoricalFixtureAsync("v6-e170c4b3", expectedStartVersion: 6,
            expectsRaceRepairHold: false, expectSchemaRejection: true, mutationSql: """
            PRAGMA writable_schema = ON;
            UPDATE sqlite_master
            SET sql = replace(sql, 'DEFAULT 0', 'DEFAULT 9')
            WHERE type = 'table' AND name = 'collection_dispatcher_fairness_state';
            PRAGMA writable_schema = OFF;
            """);

    [TestMethod]
    public async Task HistoricalV18FixtureWithIncoherentOneColumnMixStopsBeforeRootReads()
        => await AssertHistoricalFixtureAsync("v18-8bbee4b0", expectedStartVersion: 18,
            expectsRaceRepairHold: true, expectSchemaRejection: true, mutationSql: """
            PRAGMA writable_schema = ON;
            UPDATE sqlite_master
            SET sql = replace(sql, '"Lane" TEXT NOT NULL', '"Lane" TEXT NOT NULL DEFAULT ''Normal''')
            WHERE type = 'table' AND name = 'collection_requests';
            PRAGMA writable_schema = OFF;
            """);

    private static async Task AssertHistoricalFixtureAsync(string fixtureName, int expectedStartVersion,
        bool expectsRaceRepairHold, bool expectSchemaRejection = false, string? mutationSql = null)
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "CollectionHistoricalSchema");
        var assetPath = Path.Combine(fixtureRoot, fixtureName + ".sqlite.gz");
        var provenancePath = Path.Combine(fixtureRoot, fixtureName + ".provenance.json");
        Assert.IsTrue(File.Exists(assetPath), $"Missing generated fixture: {fixtureName}");
        Assert.IsTrue(File.Exists(provenancePath), $"Missing fixture provenance: {fixtureName}");

        using var provenance = JsonDocument.Parse(await File.ReadAllTextAsync(provenancePath));
        var root = provenance.RootElement;
        Assert.AreEqual(expectedStartVersion, root.GetProperty("historicalVersion").GetInt32());
        Assert.AreEqual(25, root.GetProperty("currentVersion").GetInt32());
        Assert.IsFalse(root.GetProperty("preUpgrade").GetProperty("fairnessTablePresent").GetBoolean());
        Assert.IsTrue(root.GetProperty("postUpgrade").GetProperty("fairnessTablePresent").GetBoolean());
        Assert.IsTrue(root.GetProperty("sourceBytesUnchanged").GetBoolean());
        Assert.IsTrue(root.GetProperty("sentinelCreatedAtPreserved").GetBoolean());
        Assert.IsTrue(root.GetProperty("raceRepairHoldPreserved").GetBoolean());
        Assert.AreEqual(expectsRaceRepairHold,
            root.GetProperty("raceRepairHoldPresent").GetBoolean());

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "hrp-t14-api-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var extracted = Path.Combine(temporaryRoot, "collection-platform.db");
            await using (var input = File.OpenRead(assetPath))
            await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            await using (var output = File.Create(extracted))
                await gzip.CopyToAsync(output);

            Assert.AreEqual(root.GetProperty("postUpgrade").GetProperty("databaseSha256").GetString(),
                await Sha256Async(extracted));
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = extracted,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ConnectionString);
            await connection.OpenAsync();
            var history = new List<int>();
            await using (var historyCommand = connection.CreateCommand())
            {
                historyCommand.CommandText = "SELECT version FROM collection_schema_history ORDER BY version;";
                await using var reader = await historyCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync()) history.Add(reader.GetInt32(0));
            }
            CollectionAssert.AreEqual(Enumerable.Range(expectedStartVersion, 26 - expectedStartVersion).ToArray(),
                history.ToArray());

            await using (var fairnessCommand = connection.CreateCommand())
            {
                fairnessCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'collection_dispatcher_fairness_state';";
                var fairnessSql = Convert.ToString(await fairnessCommand.ExecuteScalarAsync());
                StringAssert.Contains(fairnessSql ?? string.Empty, "CHECK (StateId = 1)");
                StringAssert.Contains(fairnessSql ?? string.Empty, "DEFAULT 0");
            }
            if (expectsRaceRepairHold)
            {
                await using var holdCommand = connection.CreateCommand();
                holdCommand.CommandText = "SELECT COUNT(*) FROM race_repair_holds WHERE RaceId = 't14-race' AND Generation = 1;";
                Assert.AreEqual(1L, Convert.ToInt64(await holdCommand.ExecuteScalarAsync()));
            }

            await using var fixture = await StoreFixture.CreateAsync();
            await fixture.SeedHorseAsync(OldHorseId);
            File.Copy(extracted, fixture.CollectionPath, overwrite: true);
            if (mutationSql is not null)
                await fixture.ExecuteAsync(fixture.CollectionPath, mutationSql);
            var before = await Sha256Async(fixture.CollectionPath);
            var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);
            Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
            if (expectSchemaRejection)
            {
                Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
                Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
                    result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
                Assert.IsEmpty(result.Roots);
            }
            else
            {
                Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason);
                Assert.AreEqual(MigrationStoreReadStatus.Observed,
                    result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
            }
            Assert.IsFalse(result.CanWrite);
            Assert.AreEqual(before, await Sha256Async(fixture.CollectionPath));
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, recursive: true); } catch { }
        }
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static async Task<Dictionary<string, string>> SnapshotSqliteFilesAsync(string databasePath)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            snapshot[path] = File.Exists(path) ? await Sha256Async(path) : "<absent>";
        }
        return snapshot;
    }

    private static string ComputeEvidenceHash(params string[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[sizeof(int)];
        foreach (var value in values)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ComputeEventEvidenceHash(
        IEnumerable<(long RowId, string BatchId, TypedHorseIdentityEventRecord Source,
            TypedHorseIdentityEventRecord Record)> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[sizeof(int)];
        foreach (var (rowId, batchId, _, record) in rows)
        {
            var key = rowId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var physicalFields = new[]
            {
                ("GlobalSequenceNumber", "integer:" + record.GlobalSequenceNumber.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                ("AggregateId", "text:" + record.AggregateId),
                ("AggregateName", "text:" + record.AggregateName),
                ("AggregateSequenceNumber", "integer:" + record.AggregateSequenceNumber.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                ("BatchId", "text:" + batchId),
                ("Data", "text:" + record.Data),
                ("Metadata", "text:" + record.Metadata),
            };
            foreach (var value in new[] { key }.Concat(physicalFields.SelectMany(field =>
                         new[] { field.Item1, field.Item2 })))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static HorseIdentityMigrationManifest Manifest() => new(
    [
        new(OldHorseId, JraIdentity, HypothesisAccepted: true),
    ]);

    private static string DestinationHorseId()
    {
        var validation = HorseIdentityMigrationManifestPolicy.Validate(Manifest());
        Assert.IsTrue(validation.IsValid);
        return validation.Mappings.Single().DestinationHorseId;
    }

    internal sealed class StoreFixture : IAsyncDisposable
    {
        private static readonly string TemporaryParent = Path.Combine(Path.GetTempPath(), "hrp-horse-id-store-engine-tests");
        private readonly string _root;
        private readonly IWebHostEnvironment _environment;
        private readonly ServiceProvider _eventFlowServices;

        private StoreFixture(string root, ServiceProvider eventFlowServices)
        {
            _root = root;
            EventStorePath = Path.Combine(root, "eventstore.db");
            CollectionPath = Path.Combine(root, "collection", "collection-platform.db");
            SchedulePath = Path.Combine(root, "schedule", "prediction-executions.db");
            _environment = new FixtureEnvironment(root);
            _eventFlowServices = eventFlowServices;
        }

        internal string EventStorePath { get; }
        internal string CollectionPath { get; }
        internal string QueuePath => Path.Combine(_root, "queue", "local-collection-queue.db");
        internal string SchedulePath { get; }
        internal string Root => _root;
        internal IServiceProvider EventFlowServices => _eventFlowServices;

        internal static async Task<StoreFixture> CreateAsync(bool createEventStore = true,
            bool createCollectionStore = true)
        {
            Directory.CreateDirectory(TemporaryParent);
            var root = Path.Combine(TemporaryParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var eventFlowServices = new ServiceCollection()
                .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning))
                .AddEventFlow(options => options
                    .AddDefaults(typeof(HorseAggregate).Assembly)
                    .AddDefaults(typeof(RaceAggregate).Assembly))
                .BuildServiceProvider();
            var fixture = new StoreFixture(root, eventFlowServices);
            if (createEventStore)
                await fixture.CreateEventStoreAsync();
            if (createCollectionStore)
                await fixture.CreateCollectionStoreAsync();
            await fixture.CreateScheduleStoreAsync();
            return fixture;
        }

        internal TypedHorseIdentityStoreEngine CreateEngine(string queueProvider = "Sqs")
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EventStore"] = $"Data Source={EventStorePath};Pooling=False",
            }).Build();
            var collectionOptions = Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = Path.GetDirectoryName(CollectionPath)!,
                DatabaseFileName = Path.GetFileName(CollectionPath),
            });
            var scheduleOptions = Options.Create(new PredictionScheduleOptions
            {
                StateDirectory = Path.GetDirectoryName(SchedulePath)!,
                StoreFileName = Path.GetFileName(SchedulePath),
            });
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Provider = queueProvider,
                LocalDatabasePath = QueuePath,
            });
            var readers = new TypedHorseIdentityStoreReaders(configuration, collectionOptions,
                scheduleOptions, queueOptions, _environment);
            return new(readers);
        }

        internal TypedHorseIdentityStoreEngine CreateEngineWithEventFlow(string queueProvider = "Sqs")
        {
            return new(CreateReadersWithEventFlow(queueProvider));
        }

        internal TypedHorseIdentityStoreReaders CreateReadersWithEventFlow(string queueProvider = "Sqs")
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EventStore"] = $"Data Source={EventStorePath};Pooling=False",
            }).Build();
            var collectionOptions = Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = Path.GetDirectoryName(CollectionPath)!,
                DatabaseFileName = Path.GetFileName(CollectionPath),
            });
            var scheduleOptions = Options.Create(new PredictionScheduleOptions
            {
                StateDirectory = Path.GetDirectoryName(SchedulePath)!,
                StoreFileName = Path.GetFileName(SchedulePath),
            });
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Provider = queueProvider,
                LocalDatabasePath = QueuePath,
            });
            var readers = new TypedHorseIdentityStoreReaders(configuration, collectionOptions,
                scheduleOptions, queueOptions, _environment,
                _eventFlowServices.GetRequiredService<IEventJsonSerializer>(),
                _eventFlowServices.GetRequiredService<IDomainEventFactory>());
            return readers;
        }

        internal async Task SeedHorseAsync(string horseId)
        {
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var horse = connection.CreateCommand())
            {
                horse.Transaction = (SqliteTransaction)transaction;
                horse.CommandText = """
                    INSERT INTO Horses
                        (HorseId, RegisteredName, NormalizedName, SexCode, BirthDate, OwnerName, BreederName,
                         SireName, DamName, DamsireName, CoatColor, Aliases)
                    VALUES ($id, 'Fixture Horse', 'FixtureHorse', NULL, NULL, NULL, NULL,
                            NULL, NULL, NULL, NULL, '[]');
                    """;
                horse.Parameters.AddWithValue("$id", horseId);
                await horse.ExecuteNonQueryAsync();
            }
            await using (var eventRow = connection.CreateCommand())
            {
                eventRow.Transaction = (SqliteTransaction)transaction;
                eventRow.CommandText = """
                    INSERT INTO EventEntity
                        (AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                    VALUES ($id, 'HorseAggregate', 1, $batch, '{}', '{}');
                    """;
                eventRow.Parameters.AddWithValue("$id", horseId);
                eventRow.Parameters.AddWithValue("$batch", Guid.NewGuid().ToString());
                await eventRow.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        internal async Task SeedJraResourceAsync(string resourceId)
        {
            await using var connection = new SqliteConnection($"Data Source={CollectionPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO collection_resources (Type, Provider, ResourceId, AttributesJson, CreatedAt)
                VALUES ('Horse', 'JRA', $resource, '{}', '2026-10-14 00:00:00');
                """;
            command.Parameters.AddWithValue("$resource", resourceId);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task SeedLocalQueueMessageAsync(string body)
        {
            _ = new LocalCollectionQueue(QueuePath);
            await using var connection = new SqliteConnection($"Data Source={QueuePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO local_collection_messages
                    (body, visible_at, receipt_handle, receive_count, dead_letter, created_at)
                VALUES ($body, '2026-10-14 12:00:00.000', NULL, 0, 0, '2026-10-14 11:59:00.000');
                """;
            command.Parameters.AddWithValue("$body", body);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<(HorseIdentityStorePlan Result, TypedHorseIdentityPageEvidence Page)>
            InspectLocalQueueHashAsync()
        {
            var result = await CreateEngineWithEventFlow("Local")
                .InspectAsync(Manifest(), CancellationToken.None);
            var page = result.TypedReferenceEvidence.Single(evidence =>
                evidence.Table == "local_collection_messages");
            return (result, page);
        }

        internal async Task<string> ComputeLocalQueueEvidenceHashAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={QueuePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT rowid, message_id, body, visible_at, receipt_handle,
                       receive_count, dead_letter, created_at
                FROM local_collection_messages ORDER BY rowid;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (await reader.ReadAsync())
            {
                var rowId = reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var fields = new (string Name, string Value)[]
                {
                    ("message_id", "integer:" + reader.GetInt64(1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("body", "text:" + reader.GetString(2)),
                    ("visible_at", "text:" + reader.GetString(3)),
                    ("receipt_handle", reader.IsDBNull(4) ? "<null>" : "text:" + reader.GetString(4)),
                    ("receive_count", "integer:" + reader.GetInt64(5).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("dead_letter", "integer:" + reader.GetInt64(6).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("created_at", "text:" + reader.GetString(7)),
                };
                foreach (var value in new[] { rowId }.Concat(fields.SelectMany(field =>
                             new[] { field.Name, field.Value })))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                    var length = new byte[sizeof(int)];
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
                    hash.AppendData(length);
                    hash.AppendData(bytes);
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        internal async Task SeedRacePredictionContextAsync(string horseId, string? entryId = null)
        {
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO RacePredictionContexts
                    (RaceId, Status, Entries, WeatherObservations, TrackConditionObservations, OddsSnapshots)
                VALUES ('race-typed-reference', 0, $entries, '[]', '[]', '[]');
                """;
            command.Parameters.AddWithValue("$entries", $$"""
                [{"entryId":"{{entryId ?? "manual-entry-001"}}","horseId":"{{horseId}}","horseNumber":1,"jockeyId":"jockey-1","trainerId":"trainer-1","gateNumber":1,"assignedWeight":55.0,"sexCode":"M","age":4,"declaredWeight":480.0,"declaredWeightDiff":0.0,"runningStyleCode":"Front","ownerName":"Owner","participationStatus":0}]
                """);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<string[]> ReadRacePredictionContextAsync(string raceId)
        {
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT RaceId, Entries, WeatherObservations, TrackConditionObservations, OddsSnapshots FROM RacePredictionContexts WHERE RaceId = $raceId;";
            command.Parameters.AddWithValue("$raceId", raceId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            return Enumerable.Range(0, reader.FieldCount).Select(reader.GetString).ToArray();
        }

        internal async Task<string> ComputeRacePredictionContextExpectedAfterHashAsync(
            string raceId, string expectedEntries)
        {
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            var columns = new List<string>();
            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = "PRAGMA table_info(\"RacePredictionContexts\");";
                await using var schemaReader = await schema.ExecuteReaderAsync();
                while (await schemaReader.ReadAsync()) columns.Add(schemaReader.GetString(1));
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT rowid,* FROM RacePredictionContexts WHERE RaceId = $raceId;";
            command.Parameters.AddWithValue("$raceId", raceId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            var values = new List<string> { reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture) };
            for (var index = 0; index < columns.Count; index++)
            {
                values.Add(columns[index]);
                var value = reader.GetValue(index + 1);
                values.Add(columns[index] == "Entries" ? "text:" + expectedEntries : StableSqlEvidenceValue(value));
            }
            return ComputeEvidenceHash(values.ToArray());
        }

        private static string StableSqlEvidenceValue(object value) => value switch
        {
            DBNull => "<null>",
            string text => "text:" + text,
            byte[] bytes => "blob:" + Convert.ToBase64String(bytes),
            long number => "integer:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int number => "integer:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double number => "real:" + number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.GetType().FullName + ":" + Convert.ToString(value,
                System.Globalization.CultureInfo.InvariantCulture),
        };

        internal async Task<string> ReadProjectionJsonAsync(
            string table, string column, string keyColumn, string key)
        {
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT \"{column}\" FROM \"{table}\" WHERE \"{keyColumn}\" = $key;";
            command.Parameters.AddWithValue("$key", key);
            var value = await command.ExecuteScalarAsync();
            Assert.IsNotNull(value);
            return (string)value;
        }

        internal async Task<(string AggregateId, string RaceId)> SeedSerializedTicketStreamAsync(string horseId)
        {
            var aggregateId = "predictionticket-00000000-0000-4000-8000-000000000201";
            var raceId = "race-ticket-context-201";
            IAggregateEvent[] aggregateEvents =
            [
                new PredictionTicketCreated(raceId, "fixture", "predictor-201", .75m,
                    $"summary {horseId}", $"fingerprint {horseId}"),
                new PredictionMarkAdded(
                    HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
                        .BuildRaceEntryId(raceId, horseId), "◎", 1, 0.9m, $"comment {horseId}"),
            ];
            var factory = _eventFlowServices.GetRequiredService<IDomainEventFactory>();
            var serializer = _eventFlowServices.GetRequiredService<IEventJsonSerializer>();
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            for (var index = 0; index < aggregateEvents.Length; index++)
            {
                var sequence = index + 1;
                var aggregateEvent = aggregateEvents[index];
                var metadata = new Metadata(new Dictionary<string, string>
                {
                    ["aggregate_id"] = aggregateId,
                    ["aggregate_name"] = nameof(PredictionTicketAggregate),
                    ["aggregate_sequence_number"] = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["global_sequence_number"] = (index + 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["batch_id"] = "ticket-fixture-batch",
                    ["event_name"] = aggregateEvent.GetType().Name,
                    ["event_version"] = "1",
                    ["event_id"] = $"event-{Guid.NewGuid():D}",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                });
                var domainEvent = factory.Create(aggregateEvent, metadata, aggregateId, sequence);
                var serialized = serializer.Serialize(domainEvent);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO EventEntity
                        (GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                    VALUES ($global, $aggregate, $name, $sequence, $batch, $data, $metadata);
                    """;
                command.Parameters.AddWithValue("$global", index + 2);
                command.Parameters.AddWithValue("$aggregate", aggregateId);
                command.Parameters.AddWithValue("$name", nameof(PredictionTicketAggregate));
                command.Parameters.AddWithValue("$sequence", sequence);
                command.Parameters.AddWithValue("$batch", "ticket-fixture-batch");
                command.Parameters.AddWithValue("$data", serialized.SerializedData);
                command.Parameters.AddWithValue("$metadata", serialized.SerializedMetadata);
                await command.ExecuteNonQueryAsync();
            }
            return (aggregateId, raceId);
        }

        internal async Task<IReadOnlyList<(long RowId, string BatchId, TypedHorseIdentityEventRecord Record)>> ReadEventRowsAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT rowid, GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber,
                       BatchId, Data, Metadata
                FROM EventEntity ORDER BY AggregateId, AggregateSequenceNumber, rowid;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<(long, string, TypedHorseIdentityEventRecord)>();
            while (await reader.ReadAsync())
                rows.Add((reader.GetInt64(0), reader.GetString(5),
                    new TypedHorseIdentityEventRecord(reader.GetInt64(1), reader.GetString(2),
                        reader.GetString(3), reader.GetInt32(4), reader.GetString(6), reader.GetString(7))));
            return rows;
        }

        internal async Task SeedSerializedHorseEventAsync(string horseId)
        {
            var metadata = new Metadata(new Dictionary<string, string>
            {
                ["aggregate_id"] = horseId,
                ["aggregate_name"] = nameof(HorseAggregate),
                ["aggregate_sequence_number"] = "1",
                ["global_sequence_number"] = "1",
                ["batch_id"] = "fixture-batch",
                ["event_name"] = nameof(HorseRegistered),
                ["event_version"] = "1",
                ["event_id"] = $"event-{Guid.NewGuid():D}",
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            });
            var factory = _eventFlowServices.GetRequiredService<IDomainEventFactory>();
            var serializer = _eventFlowServices.GetRequiredService<IEventJsonSerializer>();
            var domainEvent = factory.Create(new HorseRegistered("Fixture", "Fixture"), metadata, horseId, 1);
            var serialized = serializer.Serialize(domainEvent);
            await ExecuteAsync(EventStorePath, $"""
                DELETE FROM EventEntity;
                INSERT INTO EventEntity
                    (GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                VALUES (1, '{horseId}', '{nameof(HorseAggregate)}', 1, 'fixture-batch',
                    '{serialized.SerializedData.Replace("'", "''")}', '{serialized.SerializedMetadata.Replace("'", "''")}');
                """);
        }

        internal async Task SeedSerializedJockeyTrainerEventsAsync()
        {
            var factory = _eventFlowServices.GetRequiredService<IDomainEventFactory>();
            var serializer = _eventFlowServices.GetRequiredService<IEventJsonSerializer>();
            var events = new (string Id, string Name, IAggregateEvent Event)[]
            {
                ("jockey-00000000-0000-4000-8000-000000000301", nameof(JockeyAggregate),
                    new JockeyRegistered("Fixture Jockey", "fixture jockey")),
                ("trainer-00000000-0000-4000-8000-000000000302", nameof(TrainerAggregate),
                    new TrainerRegistered("Fixture Trainer", "fixture trainer")),
            };
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            for (var index = 0; index < events.Length; index++)
            {
                var (aggregateId, aggregateName, aggregateEvent) = events[index];
                var metadata = new Metadata(new Dictionary<string, string>
                {
                    ["aggregate_id"] = aggregateId,
                    ["aggregate_name"] = aggregateName,
                    ["aggregate_sequence_number"] = "1",
                    ["global_sequence_number"] = (index + 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["batch_id"] = "subject-fixture-batch",
                    ["event_name"] = aggregateEvent.GetType().Name,
                    ["event_version"] = "1",
                    ["event_id"] = $"event-{Guid.NewGuid():D}",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                });
                var domainEvent = factory.Create(aggregateEvent, metadata, aggregateId, 1);
                var serialized = serializer.Serialize(domainEvent);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO EventEntity
                        (GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                    VALUES ($global, $aggregate, $name, 1, 'subject-fixture-batch', $data, $metadata);
                    """;
                command.Parameters.AddWithValue("$global", index + 2);
                command.Parameters.AddWithValue("$aggregate", aggregateId);
                command.Parameters.AddWithValue("$name", aggregateName);
                command.Parameters.AddWithValue("$data", serialized.SerializedData);
                command.Parameters.AddWithValue("$metadata", serialized.SerializedMetadata);
                await command.ExecuteNonQueryAsync();
            }
        }

        internal async Task SeedInterleavedTicketStreamAcrossPageBoundaryAsync(string entryId)
        {
            var events = new List<(string AggregateId, string AggregateName, int Sequence, IAggregateEvent Event)>
            {
                ("predictionticket-00000000-0000-4000-8000-000000000201", nameof(PredictionTicketAggregate), 1,
                    new PredictionTicketCreated("202609190101", "fixture", "preserve-predictor", .5m,
                        "preserve summary", "preserve fingerprint")),
            };
            for (var index = 0; index < 126; index++)
            {
                var id = $"00000000-0000-4000-8000-{index + 301:D12}";
                if (index % 2 == 0)
                    events.Add(($"jockey-{id}", nameof(JockeyAggregate), 1,
                        new JockeyRegistered("Preserve Jockey", "preserve-jockey")));
                else
                    events.Add(($"trainer-{id}", nameof(TrainerAggregate), 1,
                        new TrainerRegistered("Preserve Trainer", "preserve-trainer")));
            }
            events.Add(("predictionticket-00000000-0000-4000-8000-000000000201", nameof(PredictionTicketAggregate), 2,
                new PredictionMarkAdded(entryId, "A", 1, .9m, "preserve event comment")));

            var factory = _eventFlowServices.GetRequiredService<IDomainEventFactory>();
            var serializer = _eventFlowServices.GetRequiredService<IEventJsonSerializer>();
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            for (var index = 0; index < events.Count; index++)
            {
                var (aggregateId, aggregateName, sequence, aggregateEvent) = events[index];
                var metadata = new Metadata(new Dictionary<string, string>
                {
                    ["aggregate_id"] = aggregateId,
                    ["aggregate_name"] = aggregateName,
                    ["aggregate_sequence_number"] = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["global_sequence_number"] = (index + 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["batch_id"] = "interleaved-page-fixture",
                    ["event_name"] = aggregateEvent.GetType().Name,
                    ["event_version"] = "1",
                    ["event_id"] = $"event-{Guid.NewGuid():D}",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                });
                var domainEvent = factory.Create(aggregateEvent, metadata, aggregateId, sequence);
                var serialized = serializer.Serialize(domainEvent);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO EventEntity
                        (GlobalSequenceNumber, AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                    VALUES ($global, $aggregate, $name, $sequence, 'interleaved-page-fixture', $data, $metadata);
                    """;
                command.Parameters.AddWithValue("$global", index + 2);
                command.Parameters.AddWithValue("$aggregate", aggregateId);
                command.Parameters.AddWithValue("$name", aggregateName);
                command.Parameters.AddWithValue("$sequence", sequence);
                command.Parameters.AddWithValue("$data", serialized.SerializedData);
                command.Parameters.AddWithValue("$metadata", serialized.SerializedMetadata);
                await command.ExecuteNonQueryAsync();
            }
        }

        internal async Task SeedMemoAndTicketProjectionsAsync(string horseId)
        {
            await ExecuteAsync(EventStorePath, $$"""
                INSERT INTO MemoSubjects (SubjectKey, Memos) VALUES
                    ('Horse:{{horseId}}', '[{"memoId":"memo-001","authorId":"author-001","memoType":"Note","content":"keep","createdAt":"2026-10-14T00:00:00+09:00","subjects":[{"subjectType":"Horse","subjectId":"{{horseId}}"}],"links":[]}]' );
                INSERT INTO PredictionTickets
                    (PredictionTicketId, RaceId, PredictorType, PredictorId, ConfidenceScore, SummaryComment,
                     PredictedAt, TicketStatus, Marks, Evaluations, EvaluationStatus)
                VALUES ('ticket-001', 'race-001', 'fixture', 'predictor-001', 0.5, 'keep',
                    '2026-10-14T00:00:00+09:00', 0,
                    '[{"entryId":"manual-entry-001","markCode":"A","predictedRank":1,"score":0.9,"comment":"keep"}]',
                    '[]', 0);
                """);
        }

        internal async Task ExecuteAsync(string path, string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        internal async Task CreateSourceProvenV19HybridCollectionAsync(string sourceFairnessSql)
        {
            // This is the source-generated EF-first v19 seed: create only the base EF
            // schema, replace its fairness table with the immutable DDL captured from
            // the v19 source runner, seed the provenance marker before migration, then
            // let the real current migrator continue through versions 20..25.
            Directory.CreateDirectory(Path.GetDirectoryName(CollectionPath)!);
            await using (var seedConnection = new SqliteConnection($"Data Source={CollectionPath};Pooling=False"))
            {
                await seedConnection.OpenAsync();
                await using var seed = seedConnection.CreateCommand();
                const string fairnessPrefix = "CREATE TABLE \"collection_dispatcher_fairness_state\"";
                var fairnessStart = SourceProvenV19SchemaSql.IndexOf(fairnessPrefix, StringComparison.Ordinal);
                var fairnessEnd = SourceProvenV19SchemaSql.IndexOf(";\nCREATE TABLE", fairnessStart,
                    StringComparison.Ordinal) + 1;
                Assert.IsGreaterThanOrEqualTo(0, fairnessStart);
                Assert.IsTrue(fairnessEnd > fairnessStart);
                var schemaSql = SourceProvenV19SchemaSql.Remove(fairnessStart, fairnessEnd - fairnessStart)
                    .Insert(fairnessStart, sourceFairnessSql + ";\n");
                seed.CommandText = schemaSql + """
                    DELETE FROM collection_schema_history;
                    INSERT INTO collection_schema_history (version, applied_at)
                    VALUES (19, 'source-v19-ef-first');
                    INSERT INTO collection_dispatcher_fairness_state
                        (StateId, ConsecutiveRealtime, LastGrantedLane, LastNonRealtimeLane,
                         ReservationSequence, ScanAvailableAt, ScanOutboxId, UpdatedAt)
                    VALUES (1, 37, 'Realtime', 'Normal', 41, NULL, NULL, '2026-10-18 00:00:00');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            _ = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = Path.GetDirectoryName(CollectionPath)!,
                DatabaseFileName = Path.GetFileName(CollectionPath),
            }));

            await using var connection = new SqliteConnection($"Data Source={CollectionPath};Pooling=False");
            await connection.OpenAsync();
            await using var history = connection.CreateCommand();
            history.CommandText = "SELECT version FROM collection_schema_history ORDER BY version;";
            await using var reader = await history.ExecuteReaderAsync();
            var versions = new List<int>();
            while (await reader.ReadAsync()) versions.Add(reader.GetInt32(0));
            CollectionAssert.AreEqual(Enumerable.Range(19, 7).ToArray(), versions.ToArray());
        }

        internal async Task NormalizeCollectionSqliteSidecarsAsync()
        {
            await using var connection = new SqliteConnection($"Data Source={CollectionPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<string> SourceSnapshotAsync()
        {
            var eventHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(EventStorePath)));
            var collectionHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(CollectionPath)));
            var scheduleHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(SchedulePath)));
            return $"{eventHash}:{collectionHash}:{scheduleHash}";
        }

        private async Task CreateEventStoreAsync()
        {
            var options = new DbContextOptionsBuilder<EventStoreDbContext>()
                .UseSqlite($"Data Source={EventStorePath};Pooling=False").Options;
            await using var context = new EventStoreDbContext(options);
            await context.Database.MigrateAsync();
        }

        private async Task CreateCollectionStoreAsync()
        {
            _ = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = Path.GetDirectoryName(CollectionPath)!,
                DatabaseFileName = Path.GetFileName(CollectionPath),
            }));
        }

        private async Task CreateScheduleStoreAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SchedulePath)!);
            _ = new PredictionScheduleStore(Options.Create(new PredictionScheduleOptions
            {
                StateDirectory = Path.GetDirectoryName(SchedulePath)!,
                StoreFileName = Path.GetFileName(SchedulePath),
            }));
        }

        public ValueTask DisposeAsync()
        {
            _eventFlowServices.Dispose();
            if (!Path.GetFullPath(_root).StartsWith(Path.GetFullPath(TemporaryParent) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to delete a path outside the owned test root.");
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private sealed class FixtureEnvironment(string root) : IWebHostEnvironment
        {
            public string ApplicationName { get; set; } = "HorseIdentityMigrationStoreEngineTests";
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string WebRootPath { get; set; } = root;
            public string EnvironmentName { get; set; } = "Testing";
            public string ContentRootPath { get; set; } = root;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}

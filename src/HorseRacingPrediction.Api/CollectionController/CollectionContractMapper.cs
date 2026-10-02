using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.CollectionController;

internal static class CollectionContractMapper
{
    internal static KnownRecoveryPreviewDto ToDto(CollectionKnownRecoveryPreview value)
        => new(value.RecipeId, value.MatchingFindingCount, value.CanaryLimit, value.RecoveryEnabled,
            value.MaintenanceMode, value.SafeToApply, value.BlockingReason);
    internal static KnownRecoveryExecutionDto ToDto(CollectionKnownRecoveryExecution value)
        => new(value.RecipeId, value.BackupId, value.BackupFileName, value.Examined, value.Recovered,
            value.Reused, value.Suppressed, value.Skipped, value.Failed);
    internal static OwnerIdentityMigrationPreviewDto ToDto(OwnerIdentityMigrationPreview value)
        => new(value.DistinctOwnerNames, value.CanonicalIds, value.LegacyIds, value.AliasMappedNames,
            value.Samples.Select(x => new OwnerIdentityMigrationSampleDto(x.DisplayName, x.CanonicalId,
                x.LegacyId, x.HasAliasMapping)).ToArray());
    internal static CollectionResourceKeyDto ToDto(ResourceKey value) => new(value.Type, value.Provider, value.Id);
    internal static ResourceKey ToInternal(CollectionResourceKeyDto value) => new(value.Type, value.Provider, value.Id);
    internal static RevisionImpactRequest ToInternal(RevisionImpactInputDto value)
        => new(value.ScopeType, value.Resources?.Select(ToInternal).ToArray(), value.From, value.To, value.NamedCondition);
    internal static BulkCollectionOperationRequest ToInternal(PreviewCollectionTaskBatchInputDto value)
        => new(value.DefinitionId, value.RequestedRevision, value.Reason, value.Selection, value.Provider,
            value.Resources?.Select(ToInternal).ToArray(), value.From, value.To, value.TrainerId,
            value.LastCollectedBefore, value.ImpactRevision, value.ExpectedResources?.Select(ToInternal).ToArray(),
            value.BatchId, value.Lane, value.Priority);
    internal static CollectionDefinitionIdDto ToDto(CollectionDefinitionId value) => new(value.Value);
    internal static CollectionRequestReceiptDto ToDto(CollectionRequestReceipt value)
        => new(value.RequestId, value.TaskId, value.CreatedTask, value.DeferredByRepairHold);
    internal static CollectionTaskAcquireResultDto ToDto(CollectionTaskAcquireResult value)
        => new(value.Status, value.Task is null ? null : ToDto(value.Task));
    internal static LeasedCollectionTaskDto ToDto(LeasedCollectionTask value)
        => new(value.TaskId, value.RequestId, ToDto(value.Resource), ToDto(value.Definition),
            value.RequestedRevision, value.Reason, value.Lane, value.Priority, value.LeaseToken,
            value.LeaseExpiresAt, value.EffectiveDate, value.Attributes,
            value.Locations?.Select(ToDto).ToArray(), value.RaceHoldGeneration, value.EntryAssignmentFingerprint);
    internal static CollectionExecutionAcquireResultDto ToDto(CollectionExecutionAcquireResult value)
        => new(value.Status, value.ExecutionBatchId, value.LeaseToken,
            value.Envelope is null ? null : ToDto(value.Envelope), value.StartBefore,
            value.NoWorkReason, value.ReservationReleaseOutcome);
    internal static CollectionDispatchEnvelopeDto ToDto(CollectionDispatchEnvelope value)
        => new(value.EnvelopeId, new(value.Compatibility.Provider, ToDto(value.Compatibility.Definition),
                value.Compatibility.EffectiveDate, value.Compatibility.Lane,
                value.Compatibility.GroupKind, value.Compatibility.GroupKey),
            value.Tasks.Select(x => new CollectionDispatchTaskReferenceDto(x.TaskId, x.DispatchGeneration)).ToArray(),
            value.ContractVersion);
    internal static ResourceLocationCandidateDto ToDto(ResourceLocationCandidate value)
        => new(value.LocationId, value.Url, value.Source, value.Status, value.LastVerifiedAt, value.Artifact);
    internal static ResourceLocationOutcomeDto ToDto(ResourceLocationOutcome value)
        => new(value.LocationId, value.Result, value.ErrorCode, value.Artifact);
    internal static ResourceLocationOutcome ToInternal(ResourceLocationOutcomeDto value)
        => new(value.LocationId, value.Result, value.ErrorCode, value.Artifact);
    internal static RaceArtifactSnapshotDto ToDto(RaceArtifactSnapshot value)
        => new(value.Artifact, value.Status, value.AppliedRevision, value.RequiredRevision,
            value.LastObservedAt, value.LastPersistedAt, value.NextDueAt, value.ErrorCode, value.ErrorMessage);
    internal static RaceSchedulingEvidenceDto ToDto(RaceSchedulingEvidence value)
        => new(value.OfficialStartAt, value.Provenance, value.VerifiedAt);
    internal static CollectionStageOutcomeDto ToDto(CollectionStageOutcome value)
        => new(value.Stage, value.Artifact, value.Result, value.ErrorCode, value.ErrorMessage,
            value.RequestedUrl, value.FinalUrl, value.Persisted);
    internal static CollectionStageOutcome ToInternal(CollectionStageOutcomeDto value)
        => new(value.Stage, value.Artifact, value.Result, value.ErrorCode, value.ErrorMessage,
            value.RequestedUrl, value.FinalUrl, value.Persisted);
    internal static RaceSchedulingEvidence ToInternal(RaceSchedulingEvidenceDto value)
        => new(value.OfficialStartAt, value.Provenance, value.VerifiedAt);
    internal static CollectionAttemptCompletion ToInternal(CompleteCollectionTaskAttemptInputDto value)
        => new(value.Result, value.ErrorCode, value.ErrorMessage,
            Uri.TryCreate(value.RequestedUrl, UriKind.Absolute, out var requested) ? requested : null,
            Uri.TryCreate(value.FinalUrl, UriKind.Absolute, out var final) ? final : null,
            value.HttpStatusCode, value.PageIdentification, value.RetryAt, value.NextCollectionAt,
            value.LocationOutcomes?.Select(ToInternal).ToArray(), value.FailureImpact,
            value.StageOutcomes?.Select(ToInternal).ToArray(),
            value.RaceEvidence is null ? null : ToInternal(value.RaceEvidence));
    internal static CollectionAttemptStageSummaryDto ToDto(CollectionAttemptStageSummary value)
        => new(value.StageOutcomeId, value.AttemptId, value.Stage, value.Artifact, value.Result,
            value.ErrorCode, value.ErrorMessage, value.RequestedUrl, value.FinalUrl, value.Persisted);
    internal static CollectionFailureNotificationDto ToDto(PendingCollectionFailureNotification value)
        => new(value.NotificationId, value.TaskId, ToDto(value.Resource), ToDto(value.Definition), value.Status,
            value.ErrorCode, value.ErrorMessage, value.AttemptCount, value.FailedAt,
            value.ResolutionStatus, value.RecoveryTaskId, value.RecoveryStartedAt, value.ResolvedAt);
    internal static CollectionTaskSummaryDto ToDto(CollectionTaskSummary value)
        => new(value.TaskId, ToDto(value.Resource), ToDto(value.Definition), value.Status, value.Lane,
            value.Priority, value.RequestedRevision, value.AvailableAt, value.AttemptCount, value.Metadata);
    internal static CollectionRequestSummaryDto ToDto(CollectionRequestSummary value)
        => new(value.RequestId, value.RequestedRevision, value.Reason, value.RequestedAt,
            value.ExplicitUrl, value.BatchId);
    internal static CollectionAttemptSummaryDto ToDto(CollectionAttemptSummary value)
        => new(value.AttemptId, value.TaskId, value.AttemptNumber, value.StartedAt, value.FinishedAt,
            value.Result, value.ErrorCode, value.ErrorMessage, value.RequestedUrl, value.FinalUrl,
            value.HttpStatusCode, value.PageIdentification, value.ExecutionBatchId, value.DispatchEnvelopeId,
            value.QueueMessageId, value.LambdaRequestId, value.BatchTaskOrdinal, value.BatchTaskCount);
    internal static CollectionStateSnapshotDto ToDto(CollectionStateSnapshot value)
        => new(ToDto(value.Resource), ToDto(value.Definition), value.AppliedRevision, value.RequiredRevision,
            value.LastCollectedAt, value.NextCollectionAt, value.Status,
            value.RaceArtifacts?.Select(ToDto).ToArray());
    internal static CollectionTaskPageDto ToDto(CollectionTaskPage value)
        => new(value.TotalCount, value.Page, value.PageSize, value.Items.Select(ToDto).ToArray());
    internal static CollectionStatePageDto ToDto(CollectionStatePage value)
        => new(value.TotalCount, value.Page, value.PageSize, value.Items.Select(ToDto).ToArray());
    internal static CollectionTaskViewCountsDto ToDto(CollectionTaskViewCounts value) => new(value.Counts);
    internal static CollectionProgressSnapshotDto ToDto(CollectionProgressSnapshot value)
        => new(value.ResourcesByType, value.StatesByStatus, value.ActiveTasksByLane,
            value.ActiveTasksByPriority, value.StatesByDefinition, value.RetryWaiting,
            (value.LaneActivity ?? []).Select(x => new CollectionLaneActivityDto(x.Lane, x.DueReady, x.Running,
                x.LastStartedAt, x.LastCompletedAt)).ToArray());
    internal static CollectionReadinessSnapshotDto ToDto(CollectionReadinessSnapshot value)
        => new(value.PendingHorseRequests, value.PendingJockeyRequests,
            value.PendingRaceResultRequests, value.PendingTrainerRequests);
    internal static CollectionMonitoringReportDto ToDto(CollectionMonitoringReport value)
        => new(value.Cutoff, value.CompletedAt, value.Enabled, value.ChangeRecordEnabled, value.Suppressed,
            value.SuppressionReason, value.Truncated, value.Findings.Select(x => new CollectionOperationalFindingDto(
                x.Fingerprint, x.Kind, (CollectionFindingClassificationDto)x.Classification, x.Severity,
                x.FirstObservedAt, x.LastObservedAt, x.Summary, x.Evidence, x.NextSafeOperation)).ToArray(),
            (CollectionMonitoringOutcomeDto)value.Outcome, value.DefinitionFlows?.Select(x =>
                new CollectionDefinitionFlowDiagnosticDto(x.Definition, x.Lane, x.CompatibilityKey,
                    x.Arrived, x.Dispatched, x.Completed, x.Active, x.OldestAgeMinutes, x.Classification)).ToArray());
    internal static CollectionPipelineStateDto ToDto(CollectionPipelineState value)
        => new(value.IsPaused, value.Reason, value.UpdatedAt);
    internal static CollectionResourceDetailDto ToDto(CollectionResourceDetail value)
        => new(value.State is null ? null : ToDto(value.State), value.Locations.Select(ToDto).ToArray(),
            value.Requests.Select(ToDto).ToArray(), value.Tasks.Select(ToDto).ToArray(),
            value.Attempts.Select(ToDto).ToArray(), value.RequestTotal, value.TaskTotal, value.AttemptTotal,
            value.HistoryPage, value.HistoryPageSize,
            value.LatestTask is null ? null : ToDto(value.LatestTask), value.TaskHistoryPage,
            value.AttemptHistoryPage, value.Failures?.Select(ToDto).ToArray(),
            value.RaceArtifacts?.Select(ToDto).ToArray(),
            value.RaceEvidence is null ? null : ToDto(value.RaceEvidence),
            value.StageOutcomes?.Select(ToDto).ToArray());
    internal static CollectionBulkPreviewDto ToDto(CollectionBulkPreview value)
        => new(ToDto(value.Definition), value.Revision, value.TargetCount, value.Resources.Select(ToDto).ToArray());
    internal static CollectionBulkExecutionDto ToDto(CollectionBulkExecution value)
        => new(value.BatchId, value.TargetCount, value.TasksCreated, value.Requests.Select(ToDto).ToArray());
    internal static CollectionTaskBatchSubmissionDto ToDto(CollectionTaskBatchSubmissionResponse value)
        => new(value.Mode, value.PreviewSelection is null ? null : ToDto(value.PreviewSelection),
            value.ExplicitItems);
    internal static BackfillHoleDto ToDto(BackfillHole value)
        => new(ToDto(value.Resource), ToDto(value.Definition), value.Status, value.ErrorCode, value.ErrorMessage);
    internal static BackfillBatchSnapshotDto ToDto(BackfillBatchSnapshot value)
        => new(value.BatchId, value.From, value.To, value.ExpectedDiscoveryDays, value.RegisteredDiscoveryDays,
            value.Pending, value.Running, value.Succeeded, value.Failed, value.Holes.Select(ToDto).ToArray(),
            value.CreatedAt, value.ExpansionCompletedAt);
    internal static RacePeriodRecollectionPreviewDto ToDto(RacePeriodRecollectionPreview value)
        => new(value.From, value.To, value.InclusiveDays, value.Provider);
    internal static RacePeriodRecollectionReceiptDto ToDto(RacePeriodRecollectionReceipt value)
        => new(ToDto(value.Batch), value.TasksCreated, value.TasksReused);
    internal static RevisionRecollectionExpansionDto ToDto(RevisionRecollectionExpansion value)
        => new(ToDto(value.Definition), value.Revision, value.BatchId, value.Affected,
            value.RequestsCreated, value.ExistingRequests);
    internal static CollectionRecollectionBatchDto ToDto(CollectionRecollectionBatchResponse value)
        => new(value.Mode, value.Revision is null ? null : ToDto(value.Revision),
            value.RacePeriod is null ? null : ToDto(value.RacePeriod));
    internal static RevisionRecollectionProgressDto ToDto(RevisionRecollectionProgress value)
        => new(ToDto(value.Definition), value.Revision, value.Affected, value.Completed, value.Pending, value.Failed);
    internal static RevisionImpactPreviewDto ToDto(RevisionImpactPreview value)
        => new(ToDto(value.Definition), value.Revision, new(value.Impact.ScopeType, value.Impact.ScopePayload),
            value.TotalCandidates, value.AffectedResources.Select(ToDto).ToArray());
    internal static CollectionFailureGroupDto ToDto(CollectionFailureGroup value)
        => new(value.GroupKey, ToDto(value.Definition), value.Status, value.ErrorCode, value.ErrorMessage,
            value.Count, value.FirstFailedAt, value.LastFailedAt, value.NotificationIds,
            value.SampleResources.Select(ToDto).ToArray());
    internal static CollectionFailureTargetDto ToDto(CollectionFailureTarget value)
        => new(value.NotificationId, value.TaskId, ToDto(value.Resource), ToDto(value.Definition), value.Status,
            value.ErrorCode, value.ErrorMessage, value.AttemptCount, value.FailedAt, value.RequestedUrl,
            value.FinalUrl, value.HttpStatusCode, value.PageIdentification, value.ExecutionBatchId,
            value.LambdaRequestId);
    internal static CollectionFailureGroupPageDto ToDto(CollectionFailureGroupPage value)
        => new(ToDto(value.Group), value.TotalCount, value.Page, value.PageSize, value.Search,
            value.Items.Select(ToDto).ToArray());
    internal static BackfillBatchSnapshotDto ToDtoBackfill(BackfillBatchSnapshot value) => ToDto(value);
    internal static CollectionExecutionBatchDetailDto ToDto(CollectionExecutionBatchDetail value)
        => new(value.ExecutionBatchId, value.DispatchEnvelopeId, value.QueueMessageId, value.LambdaRequestId,
            value.BatchTaskCount, value.StartedAt, value.FinishedAt, value.Tasks.Select(x =>
                new CollectionExecutionBatchTaskSummaryDto(x.TaskId, ToDto(x.Resource), ToDto(x.Definition),
                    x.Status, x.Result, x.AttemptNumber, x.BatchTaskOrdinal, x.StartedAt, x.FinishedAt)).ToArray());
    internal static RaceEntryOwnerRepairCandidateDto ToDto(RaceEntryOwnerRepairCandidate value)
        => new(value.RaceId, value.ResourceId, value.RaceName, value.RacecourseCode, value.RaceNumber,
            value.EntryCount, value.MissingOwnerCount, value.Date, value.Eligibility, value.EligibilityReason);
    internal static RaceEntryOwnerRepairPreviewDto ToDto(RaceEntryOwnerRepairPreview value)
        => new(value.Date, value.RaceCount, value.Candidates.Select(ToDto).ToArray());
    internal static RaceEntryOwnerMigrationProgressDto ToDto(RaceEntryOwnerMigrationProgress value)
        => new(value.BatchId, value.Requested, value.Corrected, value.Processing, value.Failed,
            value.Unavailable, value.Remaining, value.Candidates.Select(ToDto).ToArray(), value.Eligible,
            value.OutsideCardLookupPeriod);
    internal static RaceDetailMigrationReportDto ToDto(LegacyRaceDetailMergeReport value)
        => new(value.DryRun, value.SourceResources, value.TargetResources, value.Requests, value.Tasks,
            value.Attempts, value.Locations, value.States, value.SupplementRequests, value.Errors);
    internal static RaceEntryOwnerRepairBatchDto ToDto(RaceEntryOwnerRepairReceipt value)
        => new(value.BatchId, value.TargetCount, value.TasksCreated, value.TaskIds);
}

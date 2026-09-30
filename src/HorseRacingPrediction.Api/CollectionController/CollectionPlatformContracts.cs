using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed record CreateCollectionRequest(CollectionResourceType ResourceType, string Provider, string ResourceId,
    string DefinitionId, int RequestedRevision, CollectionReason Reason,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal,
    string? ExplicitUrl = null, string? BatchId = null, DateOnly? EffectiveDate = null,
    IReadOnlyDictionary<string, string>? Attributes = null);
public sealed record AcquireCollectionTaskRequest(long DispatchGeneration, int LeaseSeconds = 900,
    CollectionAttemptCorrelation? Correlation = null);
public sealed record HeartbeatCollectionTaskRequest(string LeaseToken, int LeaseSeconds = 900);
public sealed record PauseCollectionPipelineRequest(string? Reason);
public sealed record RecoverCollectionFailuresRequest(IReadOnlyList<Guid> NotificationIds,
    int? RequestedRevision = null, CollectionLane Lane = CollectionLane.Normal,
    int Priority = (int)CollectionPriority.Normal);
public sealed record RecoverCollectionFailureGroupRequest(int? RequestedRevision = null,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal,
    IReadOnlyList<Guid>? ExpectedNotificationIds = null);
public enum BulkCollectionSelection { SpecificResources, HorsesRacedInDateRange, HorsesByTrainer, LastCollectedBefore, RevisionImpact, Failed, Stale }
public sealed record BulkCollectionOperationRequest(string DefinitionId, int RequestedRevision,
    CollectionReason Reason, BulkCollectionSelection Selection, string Provider = "JRA",
    IReadOnlyList<ResourceKey>? Resources = null, DateOnly? From = null, DateOnly? To = null,
    string? TrainerId = null, DateTimeOffset? LastCollectedBefore = null, int? ImpactRevision = null,
    IReadOnlyList<ResourceKey>? ExpectedResources = null, string? BatchId = null,
    CollectionLane Lane = CollectionLane.Background, int Priority = (int)CollectionPriority.Background);
public sealed record RevisionImpactRequest(RevisionImpactScopeType ScopeType,
    IReadOnlyList<ResourceKey>? Resources = null, DateOnly? From = null, DateOnly? To = null,
    string? NamedCondition = null);
public sealed record RevisionImpactPreviewRequest(string DefinitionId, int Revision, RevisionImpactRequest Impact);
public sealed record ApplyCollectionRevisionRequest(string DefinitionId, int Revision, string Description,
    RevisionImpactRequest Impact);
public sealed record CollectionRevisionApplyResult(string DefinitionId, int Revision, int Affected);
public sealed record BackfillHoleRecoveryResult(int Holes, int TasksCreated);
public sealed record CollectionOperationsDashboard(CollectionProgressSnapshot Progress,
    IReadOnlyList<CollectionFailureGroup> Failures, IReadOnlyList<BackfillBatchSnapshot> Backfills,
    DateTimeOffset GeneratedAt);
public sealed record RevisionRecollectionRequest(CollectionLane Lane = CollectionLane.Background,
    int Priority = (int)CollectionPriority.Background);
public sealed record CreateBackfillBatchRequest(int Year, int Month, string Provider = "JRA", string? BatchId = null);
public sealed record CreateRacePeriodRecollectionRequest(DateOnly From, DateOnly To, string Provider = "JRA",
    string? BatchId = null);
public enum RaceEntryOwnerRepairEligibility { CardRetrievalCandidate, ExistingRequest, OutsideCardLookupPeriod }
public sealed record RaceEntryOwnerRepairCandidate(string RaceId, string ResourceId, string? RaceName,
    string RacecourseCode, int RaceNumber, int EntryCount, int MissingOwnerCount, DateOnly Date = default,
    RaceEntryOwnerRepairEligibility Eligibility = RaceEntryOwnerRepairEligibility.CardRetrievalCandidate,
    string? EligibilityReason = null);
public sealed record RaceEntryOwnerRepairPreview(DateOnly Date, int RaceCount,
    IReadOnlyList<RaceEntryOwnerRepairCandidate> Candidates);
public sealed record RaceEntryOwnerRepairRequest(DateOnly Date, IReadOnlyList<string> RaceIds, string? BatchId = null);
public sealed record RaceEntryOwnerRepairReceipt(string BatchId, int TargetCount, int TasksCreated,
    IReadOnlyList<Guid> TaskIds);
public sealed record RaceEntryOwnerMigrationProgress(string BatchId, int Requested, int Corrected,
    int Processing, int Failed, int Unavailable, int Remaining,
    IReadOnlyList<RaceEntryOwnerRepairCandidate> Candidates, int Eligible = 0, int OutsideCardLookupPeriod = 0);
public sealed record CompleteCollectionAttemptRequest(string LeaseToken, CollectionAttemptResult Result,
    string? ErrorCode = null, string? ErrorMessage = null, string? RequestedUrl = null,
    string? FinalUrl = null, int? HttpStatusCode = null, string? PageIdentification = null,
    DateTimeOffset? RetryAt = null, DateTimeOffset? NextCollectionAt = null,
    IReadOnlyList<ResourceLocationOutcome>? LocationOutcomes = null,
    CollectionFailureImpact FailureImpact = CollectionFailureImpact.StopPipeline,
    IReadOnlyList<CollectionStageOutcome>? StageOutcomes = null, RaceSchedulingEvidence? RaceEvidence = null);

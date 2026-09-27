using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

/// <summary>Typed variants accepted by POST /api/v2/admin/collection/tasks.</summary>
public sealed record CollectionTaskRequest(string Mode,
    CollectionResourceTaskRequest? Resource = null,
    CollectionSourceUrlTaskRequest? SourceUrl = null);

public sealed record CollectionResourceTaskRequest(CollectionResourceType ResourceType, string Provider,
    string ResourceId, string DefinitionId, int RequestedRevision, CollectionReason Reason,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal,
    string? ExplicitUrl = null, string? BatchId = null, DateOnly? EffectiveDate = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record CollectionSourceUrlTaskRequest(string? Url);

public sealed record CollectionTaskSubmissionResponse(string Mode, CollectionRequestReceipt Receipt,
    ResourceKey Resource, CollectionDefinitionId Definition, DateOnly? EffectiveDate,
    string? ExplicitUrl, IReadOnlyDictionary<string, string> Attributes);

/// <summary>Typed variants accepted by POST /api/v2/admin/collection/task-batches.</summary>
public sealed record CollectionTaskBatchRequest(string Mode,
    CollectionSelectionTaskBatchRequest? PreviewSelection = null,
    CollectionRequestBulkRequest? ExplicitItems = null);

public sealed record CollectionSelectionTaskBatchRequest(string DefinitionId, int RequestedRevision,
    CollectionReason Reason, string Selection, string Provider = "JRA",
    IReadOnlyList<ResourceKey>? Resources = null, DateOnly? From = null, DateOnly? To = null,
    string? TrainerId = null, DateTimeOffset? LastCollectedBefore = null,
    IReadOnlyList<ResourceKey>? ExpectedResources = null, string? BatchId = null,
    CollectionLane Lane = CollectionLane.Background, int Priority = (int)CollectionPriority.Background);

public sealed record CollectionTaskBatchSubmissionResponse(string Mode,
    CollectionBulkExecution? PreviewSelection, CollectionRequestBulkResponse? ExplicitItems);

public sealed record CollectionRecoveryBatchRequest(string SelectorType,
    IReadOnlyList<Guid>? NotificationIds = null, string? GroupKey = null,
    IReadOnlyList<Guid>? ExpectedNotificationIds = null, int? RequestedRevision = null,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal);

public sealed record CollectionRecollectionBatchRequest(string Mode, string? Definition = null, int? Revision = null,
    string? Provider = null, DateOnly? From = null, DateOnly? To = null, string? BatchId = null,
    CollectionLane? Lane = null, int? Priority = null);

public sealed record CollectionRecollectionBatchResponse(string Mode,
    RevisionRecollectionExpansion? Revision, RacePeriodRecollectionReceipt? RacePeriod);

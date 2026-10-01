namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CreateCollectionRecoveryBatchInputDto(string SelectorType,
    IReadOnlyList<Guid>? NotificationIds = null, string? GroupKey = null,
    IReadOnlyList<Guid>? ExpectedNotificationIds = null, int? RequestedRevision = null,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal);

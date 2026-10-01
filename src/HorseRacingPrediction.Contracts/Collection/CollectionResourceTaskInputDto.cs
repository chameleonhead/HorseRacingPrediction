namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionResourceTaskInputDto(CollectionResourceType ResourceType, string Provider,
    string ResourceId, string DefinitionId, int RequestedRevision, CollectionReason Reason,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal,
    string? ExplicitUrl = null, string? BatchId = null, DateOnly? EffectiveDate = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

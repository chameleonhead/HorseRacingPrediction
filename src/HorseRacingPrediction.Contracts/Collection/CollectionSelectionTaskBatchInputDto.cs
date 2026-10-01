namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionSelectionTaskBatchInputDto(string DefinitionId, int RequestedRevision,
    CollectionReason Reason, string Selection, string Provider = "JRA",
    IReadOnlyList<CollectionResourceKeyDto>? Resources = null, DateOnly? From = null, DateOnly? To = null,
    string? TrainerId = null, DateTimeOffset? LastCollectedBefore = null,
    IReadOnlyList<CollectionResourceKeyDto>? ExpectedResources = null, string? BatchId = null,
    CollectionLane Lane = CollectionLane.Background, int Priority = (int)CollectionPriority.Background);

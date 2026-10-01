namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionBulkTargetDto(CollectionResourceKeyDto Resource, DateOnly? EffectiveDate = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

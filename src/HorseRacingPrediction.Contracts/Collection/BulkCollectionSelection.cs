namespace HorseRacingPrediction.Contracts.Collection;

public enum BulkCollectionSelection
{
    SpecificResources,
    HorsesRacedInDateRange,
    HorsesByTrainer,
    LastCollectedBefore,
    RevisionImpact,
    Failed,
    Stale,
}

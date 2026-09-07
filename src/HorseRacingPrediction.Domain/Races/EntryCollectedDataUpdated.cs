using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class EntryCollectedDataUpdated : AggregateEvent<RaceAggregate, RaceId>
{
    public EntryCollectedDataUpdated(
        string entryId,
        string horseId,
        decimal? declaredWeight,
        decimal? declaredWeightDiff,
        string? ownerName)
    {
        EntryId = entryId;
        HorseId = horseId;
        DeclaredWeight = declaredWeight;
        DeclaredWeightDiff = declaredWeightDiff;
        OwnerName = ownerName;
    }

    public string EntryId { get; }
    public string HorseId { get; }
    public decimal? DeclaredWeight { get; }
    public decimal? DeclaredWeightDiff { get; }
    public string? OwnerName { get; }
}

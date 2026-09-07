using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class UpdateEntryCollectedDataCommand : Command<RaceAggregate, RaceId>
{
    public UpdateEntryCollectedDataCommand(
        RaceId aggregateId, string entryId,
        decimal? declaredWeight, decimal? declaredWeightDiff, string? ownerName)
        : base(aggregateId)
    {
        EntryId = entryId;
        DeclaredWeight = declaredWeight;
        DeclaredWeightDiff = declaredWeightDiff;
        OwnerName = ownerName;
    }

    public string EntryId { get; }
    public decimal? DeclaredWeight { get; }
    public decimal? DeclaredWeightDiff { get; }
    public string? OwnerName { get; }
}

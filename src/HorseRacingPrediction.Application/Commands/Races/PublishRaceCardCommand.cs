using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class PublishRaceCardCommand : Command<RaceAggregate, RaceId>
{
    public PublishRaceCardCommand(RaceId aggregateId, int entryCount)
        : base(aggregateId)
    {
        EntryCount = entryCount;
    }

    public int EntryCount { get; }
}

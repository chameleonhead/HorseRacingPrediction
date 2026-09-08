using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RefreshCollectedRaceCommand(RaceId id, CollectedRaceData data) : Command<RaceAggregate, RaceId>(id)
{
    public CollectedRaceData Data { get; } = data;
}

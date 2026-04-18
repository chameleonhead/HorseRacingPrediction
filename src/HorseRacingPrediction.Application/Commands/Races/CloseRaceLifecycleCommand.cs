using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class CloseRaceLifecycleCommand : Command<RaceAggregate, RaceId>
{
    public CloseRaceLifecycleCommand(RaceId aggregateId) : base(aggregateId) { }
}

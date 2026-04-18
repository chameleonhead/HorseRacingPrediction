using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class StartRaceCommand : Command<RaceAggregate, RaceId>
{
    public StartRaceCommand(RaceId aggregateId) : base(aggregateId) { }
}

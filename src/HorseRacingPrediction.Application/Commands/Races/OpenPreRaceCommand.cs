using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class OpenPreRaceCommand : Command<RaceAggregate, RaceId>
{
    public OpenPreRaceCommand(RaceId aggregateId) : base(aggregateId) { }
}

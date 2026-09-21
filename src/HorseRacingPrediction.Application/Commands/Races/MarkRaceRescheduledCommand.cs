using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class MarkRaceRescheduledCommand(RaceId aggregateId, string replacementRaceId)
    : Command<RaceAggregate, RaceId>(aggregateId)
{
    public string ReplacementRaceId { get; } = replacementRaceId;
}

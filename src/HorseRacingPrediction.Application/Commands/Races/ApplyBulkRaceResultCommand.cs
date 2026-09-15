using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class ApplyBulkRaceResultCommand(RaceId id, BulkRaceResultData data)
    : Command<RaceAggregate, RaceId>(id)
{
    public BulkRaceResultData Data { get; } = data;
}

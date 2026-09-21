using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class MarkRaceRescheduledCommandHandler
    : CommandHandler<RaceAggregate, RaceId, MarkRaceRescheduledCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, MarkRaceRescheduledCommand command,
        CancellationToken cancellationToken)
    {
        aggregate.MarkRescheduled(command.ReplacementRaceId);
        return Task.CompletedTask;
    }
}

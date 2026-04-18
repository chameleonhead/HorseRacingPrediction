using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class DeclareRaceResultCommandHandler : CommandHandler<RaceAggregate, RaceId, DeclareRaceResultCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, DeclareRaceResultCommand command, CancellationToken cancellationToken)
    {
        aggregate.DeclareResult(command.WinningHorseName, command.DeclaredAt,
            command.WinningHorseId, command.StewardReportText);
        return Task.CompletedTask;
    }
}

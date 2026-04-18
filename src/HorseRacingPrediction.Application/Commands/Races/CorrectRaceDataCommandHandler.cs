using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class CorrectRaceDataCommandHandler : CommandHandler<RaceAggregate, RaceId, CorrectRaceDataCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, CorrectRaceDataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectRaceData(command.RaceName, command.RacecourseCode, command.RaceNumber,
            command.GradeCode, command.SurfaceCode, command.DistanceMeters, command.DirectionCode, command.Reason);
        return Task.CompletedTask;
    }
}

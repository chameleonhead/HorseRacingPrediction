using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class CreateRaceCommandHandler : CommandHandler<RaceAggregate, RaceId, CreateRaceCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, CreateRaceCommand command, CancellationToken cancellationToken)
    {
        aggregate.Create(command.RaceDate, command.RacecourseCode, command.RaceNumber, command.RaceName,
            command.MeetingNumber, command.DayNumber, command.GradeCode,
            command.SurfaceCode, command.DistanceMeters, command.DirectionCode);
        return Task.CompletedTask;
    }
}

using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class UpdateHorseProfileCommandHandler : CommandHandler<HorseAggregate, HorseId, UpdateHorseProfileCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, UpdateHorseProfileCommand command, CancellationToken cancellationToken)
    {
        aggregate.UpdateProfile(command.RegisteredName, command.NormalizedName, command.SexCode, command.BirthDate);
        return Task.CompletedTask;
    }
}

using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class RegisterHorseCommandHandler : CommandHandler<HorseAggregate, HorseId, RegisterHorseCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, RegisterHorseCommand command, CancellationToken cancellationToken)
    {
        aggregate.RegisterHorse(command.RegisteredName, command.NormalizedName, command.SexCode, command.BirthDate);
        return Task.CompletedTask;
    }
}

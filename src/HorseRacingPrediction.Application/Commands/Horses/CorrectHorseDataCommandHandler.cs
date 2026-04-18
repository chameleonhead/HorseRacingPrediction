using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class CorrectHorseDataCommandHandler : CommandHandler<HorseAggregate, HorseId, CorrectHorseDataCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, CorrectHorseDataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectData(command.RegisteredName, command.NormalizedName, command.SexCode, command.BirthDate, command.Reason);
        return Task.CompletedTask;
    }
}

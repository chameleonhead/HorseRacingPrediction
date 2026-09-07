using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;
namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class CollectHorseProfileCommandHandler : CommandHandler<HorseAggregate, HorseId, CollectHorseProfileCommand>
{
    public override Task ExecuteAsync(HorseAggregate aggregate, CollectHorseProfileCommand command, CancellationToken token)
    { aggregate.CollectJraProfile(command.Profile); return Task.CompletedTask; }
}

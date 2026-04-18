using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class PublishRaceCardCommandHandler : CommandHandler<RaceAggregate, RaceId, PublishRaceCardCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, PublishRaceCardCommand command, CancellationToken cancellationToken)
    {
        aggregate.PublishCard(command.EntryCount);
        return Task.CompletedTask;
    }
}

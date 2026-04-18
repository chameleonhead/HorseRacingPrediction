using EventFlow.Commands;
using HorseRacingPrediction.Domain.Jockeys;

namespace HorseRacingPrediction.Application.Commands.Jockeys;

public sealed class UpdateJockeyProfileCommandHandler : CommandHandler<JockeyAggregate, JockeyId, UpdateJockeyProfileCommand>
{
    public override Task ExecuteAsync(JockeyAggregate aggregate, UpdateJockeyProfileCommand command, CancellationToken cancellationToken)
    {
        aggregate.UpdateProfile(command.DisplayName, command.NormalizedName, command.AffiliationCode);
        return Task.CompletedTask;
    }
}

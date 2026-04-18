using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RecordTrackConditionObservationCommandHandler : CommandHandler<RaceAggregate, RaceId, RecordTrackConditionObservationCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RecordTrackConditionObservationCommand command, CancellationToken cancellationToken)
    {
        aggregate.RecordTrackConditionObservation(command.ObservationTime,
            command.TurfConditionCode, command.DirtConditionCode, command.GoingDescriptionText);
        return Task.CompletedTask;
    }
}

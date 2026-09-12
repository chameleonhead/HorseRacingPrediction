using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RecordRaceOddsSnapshotCommand(RaceId id, DateTimeOffset observedAt,
    IReadOnlyList<RaceOddsEntry> entries, IReadOnlyList<RaceOddsObservation>? observations = null) : Command<RaceAggregate, RaceId>(id)
{
    public DateTimeOffset ObservedAt { get; } = observedAt;
    public IReadOnlyList<RaceOddsEntry> Entries { get; } = entries;
    public IReadOnlyList<RaceOddsObservation>? Observations { get; } = observations;
}

public sealed class RecordRaceOddsSnapshotCommandHandler
    : CommandHandler<RaceAggregate, RaceId, RecordRaceOddsSnapshotCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RecordRaceOddsSnapshotCommand command,
        CancellationToken cancellationToken)
    {
        aggregate.RecordOddsSnapshot(command.ObservedAt, command.Entries, command.Observations);
        return Task.CompletedTask;
    }
}

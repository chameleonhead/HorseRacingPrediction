using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class RepairRaceEntryAssignmentsCommand(RaceId id, int expectedVersion,
    string operationId, string fingerprint, string sourceUrl, DateTimeOffset observedAt,
    IReadOnlyList<EntryDetails> entries, string? gradeCode, string sourceEvidenceJson) : Command<RaceAggregate, RaceId>(id)
{
    public int ExpectedVersion { get; } = expectedVersion;
    public string OperationId { get; } = operationId;
    public string Fingerprint { get; } = fingerprint;
    public string SourceUrl { get; } = sourceUrl;
    public DateTimeOffset ObservedAt { get; } = observedAt;
    public IReadOnlyList<EntryDetails> Entries { get; } = entries;
    public string? GradeCode { get; } = gradeCode;
    public string SourceEvidenceJson { get; } = sourceEvidenceJson;
}

public sealed class RepairRaceEntryAssignmentsCommandHandler
    : CommandHandler<RaceAggregate, RaceId, RepairRaceEntryAssignmentsCommand>
{
    public override Task ExecuteAsync(RaceAggregate aggregate, RepairRaceEntryAssignmentsCommand command,
        CancellationToken cancellationToken)
    {
        aggregate.RepairEntryAssignments(command.ExpectedVersion, command.OperationId, command.Fingerprint,
            command.SourceUrl, command.ObservedAt, command.Entries, command.GradeCode, command.SourceEvidenceJson);
        return Task.CompletedTask;
    }
}

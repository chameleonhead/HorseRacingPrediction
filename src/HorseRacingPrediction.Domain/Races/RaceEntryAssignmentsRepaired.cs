using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

/// <summary>One audited replacement of an entry-only race assignment; never migrates results.</summary>
public sealed class RaceEntryAssignmentsRepaired(
    string operationId, string fingerprint, string sourceUrl, DateTimeOffset observedAt, string sourceEvidenceJson,
    IReadOnlyList<EntryDetails> previousEntries, IReadOnlyList<EntryDetails> entries,
    DateOnly raceDate, string racecourseCode, string? gradeCode,
    string? surfaceCode, int? distanceMeters, string? directionCode)
    : AggregateEvent<RaceAggregate, RaceId>
{
    public string OperationId { get; } = operationId;
    public string Fingerprint { get; } = fingerprint;
    public string SourceUrl { get; } = sourceUrl;
    public DateTimeOffset ObservedAt { get; } = observedAt;
    public string SourceEvidenceJson { get; } = sourceEvidenceJson;
    public IReadOnlyList<EntryDetails> PreviousEntries { get; } = previousEntries;
    public IReadOnlyList<EntryDetails> Entries { get; } = entries;
    public DateOnly RaceDate { get; } = raceDate;
    public string RacecourseCode { get; } = racecourseCode;
    public string? GradeCode { get; } = gradeCode;
    public string? SurfaceCode { get; } = surfaceCode;
    public int? DistanceMeters { get; } = distanceMeters;
    public string? DirectionCode { get; } = directionCode;
}

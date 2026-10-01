namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceOddsSnapshotDto(DateTimeOffset ObservedAt, IReadOnlyList<RaceOddsEntrySnapshotDto> Entries,
    IReadOnlyList<RaceOddsObservationSnapshotDto>? Observations = null,
    IReadOnlyList<RaceOddsAssignmentDto>? Assignments = null);

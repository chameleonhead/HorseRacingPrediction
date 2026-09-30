
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RecordRaceOddsSnapshotRequest(DateTimeOffset ObservedAt,
    IReadOnlyList<RaceOddsEntryInputDto> Entries,
    IReadOnlyList<RaceOddsObservationInputDto>? Observations = null);

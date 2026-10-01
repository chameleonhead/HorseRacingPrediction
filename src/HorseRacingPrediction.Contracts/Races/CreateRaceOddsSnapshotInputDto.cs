namespace HorseRacingPrediction.Contracts.Races;

public sealed record CreateRaceOddsSnapshotInputDto(DateTimeOffset ObservedAt,
    IReadOnlyList<RaceOddsEntryInputDto> Entries,
    IReadOnlyList<RaceOddsObservationInputDto>? Observations = null);

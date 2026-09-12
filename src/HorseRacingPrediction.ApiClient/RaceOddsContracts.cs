namespace HorseRacingPrediction.ApiClient;

public sealed record RaceOddsEntryRequest(int HorseNumber, decimal WinOdds, int? Popularity = null);
public sealed record RaceOddsObservationRequest(string Market, string Selection, decimal Value,
    int? Popularity = null);
public sealed record RecordRaceOddsSnapshotRequest(DateTimeOffset ObservedAt,
    IReadOnlyList<RaceOddsEntryRequest> Entries,
    IReadOnlyList<RaceOddsObservationRequest>? Observations = null);

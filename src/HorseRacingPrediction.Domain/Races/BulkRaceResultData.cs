namespace HorseRacingPrediction.Domain.Races;

/// <summary>
/// A fully validated collection envelope that can be applied to one race aggregate update.
/// Related Horse, Jockey, and Trainer aggregates are deliberately outside this boundary.
/// </summary>
public sealed record BulkRaceResultData(
    DateOnly RaceDate,
    string RacecourseCode,
    int RaceNumber,
    string RaceName,
    int? EntryCount,
    string? GradeCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    IReadOnlyList<EntryDetails> Entries,
    IReadOnlyList<EntryResultDetails> EntryResults,
    string? WinningHorseName = null,
    DateTimeOffset? DeclaredAt = null,
    PayoutResultDetails? Payouts = null,
    WeatherObservationDetails? Weather = null,
    TrackConditionObservationDetails? TrackCondition = null,
    string? StewardReportText = null);

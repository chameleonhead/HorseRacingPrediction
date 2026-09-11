namespace HorseRacingPrediction.Domain.Races;

/// <summary>取得できた値だけを既存レースへ反映する入力。nullは未取得。</summary>
public sealed record CollectedRaceData(
    string RaceName, string? GradeCode, string? SurfaceCode, int? DistanceMeters, string? DirectionCode,
    int? EntryCount, IReadOnlyList<EntryDetails> Entries, IReadOnlyList<EntryResultDetails>? Results,
    string? WinningHorseName = null, string? WinningHorseId = null,
    PayoutResultDetails? Payouts = null, WeatherObservationDetails? Weather = null,
    TrackConditionObservationDetails? TrackCondition = null,
    TimeOnly? StartTime = null, string? OverallPaceText = null, string? CornerPassagesText = null, string? CourseLayout = null,
    string? StewardReportText = null);

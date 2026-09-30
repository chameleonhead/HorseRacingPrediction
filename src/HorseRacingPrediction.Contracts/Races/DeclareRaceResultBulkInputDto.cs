using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Races;

/// <summary>Race, entry, weather, track-condition and payout data submitted as one bulk operation.</summary>
public sealed record DeclareRaceResultBulkInputDto(
    [property: Required] DateOnly RaceDate,
    [property: Required, StringLength(32, MinimumLength = 2)] string RacecourseCode,
    [property: Range(1, 20)] int RaceNumber,
    [property: Required, StringLength(128, MinimumLength = 1)] string RaceName,
    int? EntryCount = null,
    string? GradeCode = null,
    string? SurfaceCode = null,
    int? DistanceMeters = null,
    string? DirectionCode = null,
    string? WinningHorseName = null,
    DateTimeOffset? DeclaredAt = null,
    IReadOnlyList<RaceResultEntryBulkDto>? Entries = null,
    RecordWeatherObservationInputDto? Weather = null,
    RecordTrackConditionInputDto? TrackCondition = null,
    DeclarePayoutResultInputDto? Payouts = null,
    string? TargetRaceId = null,
    bool RefreshExistingData = false,
    bool IsRaceCard = false,
    TimeOnly? StartTime = null,
    string? OverallPaceText = null,
    string? CornerPassagesText = null,
    string? CourseLayout = null,
    string? SourceHorseId = null,
    string? StewardReportText = null);

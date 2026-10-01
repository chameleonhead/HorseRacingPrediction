namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceRepairDetailsDto(string RaceId, DateOnly? RaceDate, string? RacecourseCode, int? RaceNumber,
    string? RaceName, RaceStatus Status, int? MeetingNumber, int? DayNumber, string? GradeCode, string? SurfaceCode,
    int? DistanceMeters, string? DirectionCode, int? EntryCount, IReadOnlyList<RaceRepairEntryDto> Entries,
    IReadOnlyList<RaceWeatherObservationDto> WeatherObservations,
    IReadOnlyList<RaceTrackConditionDto> TrackConditionObservations, string? WinningHorseName,
    string? WinningHorseId, string? StewardReportText, DateTimeOffset? ResultDeclaredAt,
    IReadOnlyList<RaceRepairEntryResultDto> EntryResults, RacePayoutResultDto? PayoutResult,
    TimeOnly? StartTime = null, string? OverallPaceText = null, string? CornerPassagesText = null,
    string? CourseLayout = null, string? ReplacementRaceId = null);

namespace HorseRacingPrediction.AgentClient.Web.ApiBrowsing;

public sealed record RaceDetailDto(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber,
    string? RaceName,
    HorseRacingPrediction.Contracts.RaceStatus Status,
    int? MeetingNumber,
    int? DayNumber,
    string? GradeCode,
    string? SurfaceCode,
    int? DistanceMeters,
    string? DirectionCode,
    int? EntryCount,
    IReadOnlyList<RaceEntryDto> Entries,
    string? WinningHorseName,
    string? WinningHorseId,
    string? StewardReportText,
    DateTimeOffset? ResultDeclaredAt,
    IReadOnlyList<RaceEntryResultDto> EntryResults);

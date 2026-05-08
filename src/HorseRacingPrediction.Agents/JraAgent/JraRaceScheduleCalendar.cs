namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>JRA サイトから抽出した開催日一覧。</summary>
public sealed record JraRaceScheduleCalendar(
    DateOnly ReferenceDate,
    IReadOnlyList<DateOnly> RaceDates,
    string SourceUrl,
    IReadOnlyList<JraRaceScheduleDay>? ScheduledDays = null,
    IReadOnlyList<JraPageParseIssue>? Issues = null);
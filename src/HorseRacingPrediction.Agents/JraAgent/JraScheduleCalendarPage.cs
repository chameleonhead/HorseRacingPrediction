namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraScheduleCalendarPage(
    string SourceUrl,
    int? Year,
    int? Month,
    IReadOnlyList<JraCalendarMonthLink> AvailableMonths,
    IReadOnlyList<JraRaceScheduleDay> ScheduledDays,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.ScheduleCalendar;
}
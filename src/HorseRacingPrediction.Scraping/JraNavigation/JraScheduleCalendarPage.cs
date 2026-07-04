namespace HorseRacingPrediction.Scraping.JraNavigation;

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
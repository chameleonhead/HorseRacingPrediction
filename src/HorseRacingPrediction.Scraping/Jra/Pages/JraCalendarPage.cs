using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

public sealed record JraCalendarPage(
    string Url,
    YearMonth Month,
    IReadOnlyList<JraRaceDate> RaceDates)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.Calendar;
}

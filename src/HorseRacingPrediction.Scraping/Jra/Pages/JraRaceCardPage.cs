using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

public sealed record JraRaceCardPage(
    string Url,
    RaceId RaceId,
    string? RaceName,
    TimeOnly? StartTime,
    IReadOnlyList<RaceEntry> Entries, RaceCourseSpec? CourseSpec = null, string? GradeCode = null)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.RaceCard;
}

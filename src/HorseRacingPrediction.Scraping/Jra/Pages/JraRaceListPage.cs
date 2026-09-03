using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

public sealed record JraRaceListPage(
    string Url,
    DateOnly Date,
    RaceCourse Course,
    IReadOnlyList<RaceSummary> Races)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.RaceList;
}

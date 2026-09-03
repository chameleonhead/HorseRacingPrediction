using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

public sealed record JraRaceResultPage(
    string Url,
    RaceId RaceId,
    string? RaceName,
    IReadOnlyList<RaceResultEntry> Results)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.RaceResult;
}

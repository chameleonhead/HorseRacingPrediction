using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

public sealed record JraOddsEntry(int HorseNumber, decimal WinOdds, int? Popularity);
public sealed record JraRaceOddsPage(string Url, RaceId RaceId, DateTimeOffset ObservedAt,
    IReadOnlyList<JraOddsEntry> Entries) : IJraPage
{
    public JraPageKind Kind => JraPageKind.RaceOdds;
}

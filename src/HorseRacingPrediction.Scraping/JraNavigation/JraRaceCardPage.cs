namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraRaceCardPage(
    string SourceUrl,
    string? RaceName,
    DateOnly? RaceDate,
    string? Racecourse,
    string? Distance,
    IReadOnlyList<string> AvailableTabs,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.RaceCard;
}
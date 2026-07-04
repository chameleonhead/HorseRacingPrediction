namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraThisWeekPage(
    string SourceUrl,
    string? DateRangeLabel,
    IReadOnlyList<JraThisWeekRaceEntry> FeaturedRaces,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.ThisWeekFeature;
}
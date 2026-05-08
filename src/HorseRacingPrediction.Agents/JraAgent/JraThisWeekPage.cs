namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraThisWeekPage(
    string SourceUrl,
    string? DateRangeLabel,
    IReadOnlyList<JraThisWeekRaceEntry> FeaturedRaces,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.ThisWeekFeature;
}
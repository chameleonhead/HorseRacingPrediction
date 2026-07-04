namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraKeibaMenuPage(
    string SourceUrl,
    IReadOnlyList<JraNavigationMenuEntry> PrimaryEntries,
    string? ScheduleEntryText,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.KeibaMenu;
}
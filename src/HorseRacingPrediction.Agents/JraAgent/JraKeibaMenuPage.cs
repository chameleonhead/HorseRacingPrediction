namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraKeibaMenuPage(
    string SourceUrl,
    IReadOnlyList<JraNavigationMenuEntry> PrimaryEntries,
    string? ScheduleEntryText,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.KeibaMenu;
}
namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraRaceListPage(
    string SourceUrl,
    DateOnly? RaceDate,
    string? Racecourse,
    IReadOnlyList<JraRaceListEntry> Races,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.RaceList;
}
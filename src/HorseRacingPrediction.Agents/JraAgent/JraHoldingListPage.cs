namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraHoldingListPage(
    string SourceUrl,
    IReadOnlyList<JraHoldingEntry> Holdings,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.HoldingList;
}
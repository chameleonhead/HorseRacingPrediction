namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraHoldingListPage(
    string SourceUrl,
    IReadOnlyList<JraHoldingEntry> Holdings,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.HoldingList;
}
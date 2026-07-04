namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraGradeOneSpecialPage(
    string SourceUrl,
    string? RaceName,
    string? Grade,
    DateOnly? RaceDate,
    string? Racecourse,
    string? Distance,
    IReadOnlyList<JraSpecialPageTab> Tabs,
    IReadOnlyList<JraSpecialPageNewsItem> RelatedNews,
    IReadOnlyList<JraPageParseIssue> Issues)
{
    public JraPageKind PageKind => JraPageKind.GradeOneSpecial;
}
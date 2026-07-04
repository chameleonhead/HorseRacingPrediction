namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraPageParseIssue(
    string Code,
    JraPageDiagnosticSeverity Severity,
    string Message);
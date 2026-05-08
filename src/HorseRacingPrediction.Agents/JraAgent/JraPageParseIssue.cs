namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraPageParseIssue(
    string Code,
    JraPageDiagnosticSeverity Severity,
    string Message);
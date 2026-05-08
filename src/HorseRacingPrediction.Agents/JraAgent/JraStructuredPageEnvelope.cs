namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraStructuredPageEnvelope(
    bool Success,
    JraPageKind PageKind,
    string SourceUrl,
    object? Data,
    IReadOnlyList<JraPageParseIssue> Issues,
    JraPageParseConfidence Confidence,
    IReadOnlyList<JraStructuredPageNextLink> RecommendedNextLinks,
    string? Error = null);
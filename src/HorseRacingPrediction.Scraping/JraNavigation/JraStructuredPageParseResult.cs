namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraStructuredPageParseResult<T>(
    bool Success,
    T? Data,
    IReadOnlyList<JraPageParseIssue> Issues,
    JraPageParseConfidence Confidence,
    IReadOnlyList<JraStructuredPageNextLink> RecommendedNextLinks,
    string? Error = null)
    where T : class;
namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraStructuredPageNextLink(
    string Relation,
    string Label,
    string? Url,
    JraStructuredLinkNavigationMode NavigationMode);
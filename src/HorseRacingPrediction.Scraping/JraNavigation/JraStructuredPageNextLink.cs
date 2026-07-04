namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraStructuredPageNextLink(
    string Relation,
    string Label,
    string? Url,
    JraStructuredLinkNavigationMode NavigationMode);
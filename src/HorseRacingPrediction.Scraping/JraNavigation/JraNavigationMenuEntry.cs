namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraNavigationMenuEntry(
    string Text,
    string? Url,
    bool IsPrimary);
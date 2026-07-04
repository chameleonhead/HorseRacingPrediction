namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraCalendarMonthLink(
    int Month,
    string Text,
    string? Url,
    bool IsCurrent);
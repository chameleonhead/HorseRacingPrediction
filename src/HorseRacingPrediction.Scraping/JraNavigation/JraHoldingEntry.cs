namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraHoldingEntry(
    string Label,
    string? Racecourse,
    int? HoldingNumber,
    int? DayNumber);
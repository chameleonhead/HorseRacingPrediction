namespace HorseRacingPrediction.Scraping.JraNavigation;

public sealed record JraRaceListEntry(
    int RaceNumber,
    string Label,
    string? Url);
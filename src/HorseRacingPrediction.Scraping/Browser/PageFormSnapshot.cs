namespace HorseRacingPrediction.Scraping.Browser;

public sealed record PageFormSnapshot(
    string Title,
    string Action,
    string Method,
    IReadOnlyList<PageFormFieldSnapshot> Fields);

namespace HorseRacingPrediction.Scraping.Browser;

public sealed record PageFormFieldSnapshot(
    string Label,
    string Name,
    PageFormFieldKind Kind,
    bool Required,
    bool Disabled,
    string? Placeholder,
    string? Value,
    IReadOnlyList<string> Options);

namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraNavigationMenuEntry(
    string Text,
    string? Url,
    bool IsPrimary);
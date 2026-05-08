namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraCalendarMonthLink(
    int Month,
    string Text,
    string? Url,
    bool IsCurrent);
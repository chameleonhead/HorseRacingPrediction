namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraHoldingEntry(
    string Label,
    string? Racecourse,
    int? HoldingNumber,
    int? DayNumber);
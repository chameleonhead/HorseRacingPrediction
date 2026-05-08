namespace HorseRacingPrediction.Agents.JraAgent;

public sealed record JraRaceListEntry(
    int RaceNumber,
    string Label,
    string? Url);
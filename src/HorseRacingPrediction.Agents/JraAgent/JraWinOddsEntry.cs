namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>単勝オッズエントリー。</summary>
public sealed record JraWinOddsEntry(
    int HorseNumber,
    string? HorseName,
    decimal? Odds,
    int? Popularity);
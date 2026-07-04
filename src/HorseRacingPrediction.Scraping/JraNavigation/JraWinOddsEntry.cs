namespace HorseRacingPrediction.Scraping.JraNavigation;

/// <summary>単勝オッズエントリー。</summary>
public sealed record JraWinOddsEntry(
    int HorseNumber,
    string? HorseName,
    decimal? Odds,
    int? Popularity);
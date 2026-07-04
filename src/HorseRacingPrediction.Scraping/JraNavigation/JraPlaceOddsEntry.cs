namespace HorseRacingPrediction.Scraping.JraNavigation;

/// <summary>複勝オッズエントリー（最小〜最大レンジ）。</summary>
public sealed record JraPlaceOddsEntry(
    int HorseNumber,
    string? HorseName,
    decimal? OddsMin,
    decimal? OddsMax);
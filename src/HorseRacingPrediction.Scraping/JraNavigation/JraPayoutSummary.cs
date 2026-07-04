namespace HorseRacingPrediction.Scraping.JraNavigation;

/// <summary>払戻金エントリー。</summary>
public sealed record JraPayoutSummary(
    string BetType,
    string Combination,
    string Payout);
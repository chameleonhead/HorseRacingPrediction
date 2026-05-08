namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>払戻金エントリー。</summary>
public sealed record JraPayoutSummary(
    string BetType,
    string Combination,
    string Payout);
namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 成績ページの払い戻し金 1 行分のデータ。
/// </summary>
/// <param name="Combination">馬番組み合わせ（例: "3", "1-3", "1-2-3"）</param>
/// <param name="Amount">払い戻し金（円）</param>
public sealed record JraPayoutEntry(string Combination, decimal Amount);

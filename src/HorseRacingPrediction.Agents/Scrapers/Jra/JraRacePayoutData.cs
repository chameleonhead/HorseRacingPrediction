namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 成績ページから抽出した払い戻し全体データ。
/// 各賭け式ごとの払い戻し一覧を保持する。
/// </summary>
public sealed record JraRacePayoutData(
    IReadOnlyList<JraPayoutEntry> WinPayouts,
    IReadOnlyList<JraPayoutEntry> PlacePayouts,
    IReadOnlyList<JraPayoutEntry> QuinellaPayouts,
    IReadOnlyList<JraPayoutEntry> WidePayouts,
    IReadOnlyList<JraPayoutEntry> ExactaPayouts,
    IReadOnlyList<JraPayoutEntry> TrioPayouts,
    IReadOnlyList<JraPayoutEntry> TrifectaPayouts);

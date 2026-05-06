namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 重賞レース一覧ページの抽出結果。
/// </summary>
public sealed record JraGradedRaceListData(
    string Url,
    int? Year,
    IReadOnlyList<JraGradedRaceItemData> Races);

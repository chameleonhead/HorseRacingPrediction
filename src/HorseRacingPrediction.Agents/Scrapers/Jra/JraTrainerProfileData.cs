namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA の調教師プロフィールページから抽出した情報。
/// </summary>
public sealed record JraTrainerProfileData(
    string DisplayName,
    string? AffiliationCode,
    int? DebutYear,
    IReadOnlyDictionary<string, string> Facts);
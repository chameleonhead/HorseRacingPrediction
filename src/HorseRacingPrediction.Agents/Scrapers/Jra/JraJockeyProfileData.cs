namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA の騎手プロフィールページから抽出した情報。
/// </summary>
public sealed record JraJockeyProfileData(
    string DisplayName,
    string? AffiliationCode,
    DateOnly? BirthDate,
    int? DebutYear,
    IReadOnlyDictionary<string, string> Facts);
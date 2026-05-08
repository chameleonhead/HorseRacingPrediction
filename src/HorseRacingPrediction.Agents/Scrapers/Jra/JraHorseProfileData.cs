namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA の競走馬プロフィールページから抽出した情報。
/// 保存可能な主要属性に加え、将来拡張用にラベル付き属性も保持する。
/// </summary>
public sealed record JraHorseProfileData(
    string RegisteredName,
    string? SexCode,
    DateOnly? BirthDate,
    string? TrainerName,
    string? OwnerName,
    string? BreederName,
    string? SireName,
    string? DamName,
    IReadOnlyDictionary<string, string> Facts);
namespace HorseRacingPrediction.Scraping.JraNavigation;

/// <summary>
/// 馬・騎手・調教師のプロフィールを統一して保持する。
/// <see cref="EntityKind"/> で種別を判別する。
/// </summary>
public sealed record JraEntityProfile(
    /// <summary>horse / jockey / trainer</summary>
    string EntityKind,
    string? DisplayName,
    /// <summary>牡・牝・セ（馬のみ）</summary>
    string? SexCode,
    DateOnly? BirthDate,
    /// <summary>美浦・栗東・地方など</summary>
    string? Affiliation,
    int? DebutYear,
    string? SireName,
    string? DamName,
    string? OwnerName,
    string? BreederName,
    string? TrainerName,
    IReadOnlyDictionary<string, string> Facts,
    string SourceUrl);
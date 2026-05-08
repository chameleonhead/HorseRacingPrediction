namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>JRA オッズページから抽出したデータ。</summary>
public sealed record JraOddsResult(
    string? RaceName,
    DateOnly? RaceDate,
    string? Racecourse,
    int? RaceNumber,
    IReadOnlyList<JraWinOddsEntry> WinOdds,
    IReadOnlyList<JraPlaceOddsEntry> PlaceOdds,
    string SourceUrl);
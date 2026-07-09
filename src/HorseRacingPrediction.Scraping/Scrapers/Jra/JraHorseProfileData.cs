using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Scraping.Scrapers.Jra;

/// <summary>
/// 競走馬情報ページから抽出したプロフィールと過去の競走成績をまとめたデータ。
/// </summary>
public sealed record JraHorseProfileData(
    JraEntityProfile Profile,
    IReadOnlyList<JraHorseRaceHistoryEntryData> RaceHistory);

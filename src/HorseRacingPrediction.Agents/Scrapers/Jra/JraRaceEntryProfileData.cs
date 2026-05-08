namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// 出馬表 1 頭分の行データと、そこから辿った詳細プロフィール群。
/// </summary>
public sealed record JraRaceEntryProfileData(
    JraRaceEntryData Entry,
    JraHorseProfileData? HorseProfile,
    JraJockeyProfileData? JockeyProfile,
    JraTrainerProfileData? TrainerProfile);
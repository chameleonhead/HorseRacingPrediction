namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// 出馬表と、その各出走馬から辿った詳細プロフィール一式。
/// </summary>
public sealed record JraRaceCardDetailData(
    JraRaceCardData RaceCard,
    IReadOnlyList<JraRaceEntryProfileData> EntryProfiles);
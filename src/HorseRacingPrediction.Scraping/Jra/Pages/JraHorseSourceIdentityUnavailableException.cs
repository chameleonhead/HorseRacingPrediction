using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

/// <summary>A provisional card cannot identify every horse independently of its number.</summary>
public sealed class JraHorseSourceIdentityUnavailableException(string url, RaceId raceId)
    : JraPageParseException(JraPageKind.RaceCard, url,
        "馬番未確定の出馬表で全頭の公式競走馬識別子を確認できません。", "HorseSourceIdentity")
{
    public RaceId RaceId { get; } = raceId;
}

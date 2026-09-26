using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

/// <summary>A recognized card whose horse and frame number cells are all unpublished.</summary>
public sealed class JraHorseNumbersUnconfirmedException(string url, RaceId raceId)
    : JraPageParseException(JraPageKind.RaceCard, url,
        "公式の馬番・枠番が未確定です。確定後に再取得します。", "HorseNumber")
{
    public RaceId RaceId { get; } = raceId;
}

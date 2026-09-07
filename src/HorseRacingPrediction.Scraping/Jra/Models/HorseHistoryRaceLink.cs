using HorseRacingPrediction.Scraping.Browser;
namespace HorseRacingPrediction.Scraping.Jra.Models;

public sealed record HorseHistoryRaceLink(DateOnly? Date, string Course, string RaceName, PageLinkSnapshot? Link, string? ExclusionReason)
{
    public string Key => $"{Date:yyyy-MM-dd}|{Course}|{RaceName}|{Link?.Url}";
}

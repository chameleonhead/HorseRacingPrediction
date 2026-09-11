using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
namespace HorseRacingPrediction.Scraping.Jra.Pages;

public sealed record JraSubjectPage(JraSubjectProfileDto Profile, IReadOnlyList<HorseHistoryRaceLink> Races,
    PageLinkSnapshot? NextPage) : IJraPage
{
    public string Url => Profile.SourceUrl;
    public JraPageKind Kind => Profile.SubjectType switch
    {
        "Horse" => JraPageKind.HorseProfile,
        "Jockey" => JraPageKind.JockeyProfile,
        _ => JraPageKind.TrainerProfile,
    };
}

namespace HorseRacingPrediction.Scraping.JraNavigation;

public static class JraStructuredLinkRelations
{
    public const string OpenSchedule = "open_schedule";
    public const string MenuEntry = "menu_entry";
    public const string OpenMonth = "open_month";
    public const string OpenHolding = "open_holding";
    public const string OpenRace = "open_race";
    public const string OpenRaceCard = "open_race_card";
    public const string OpenOdds = "open_odds";
    public const string OpenResult = "open_result";
    public const string OpenHorseInfo = "open_horse_info";
    public const string OpenData = "open_data";
    public const string OpenRelated = "open_related";

    public const string OpenSpecialPrefix = "open_special:";
    public const string OpenRaceCardPrefix = "open_race_card:";
    public const string OpenHorseInfoPrefix = "open_horse_info:";

    public static string OpenSpecial(string raceName) => OpenSpecialPrefix + raceName;
    public static string OpenRaceCardForRace(string raceName) => OpenRaceCardPrefix + raceName;
    public static string OpenHorseInfoForRace(string raceName) => OpenHorseInfoPrefix + raceName;
}
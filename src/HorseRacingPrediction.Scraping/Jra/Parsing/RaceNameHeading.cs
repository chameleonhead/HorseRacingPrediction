using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

internal static class RaceNameHeading
{
    // 開催情報だけを除外する。「中山」を含む競走名は除外しない。
    private static readonly Regex Meeting = new(@"^\d+回\s*(札幌|函館|福島|新潟|東京|中山|中京|京都|阪神|小倉)\s*\d+日$", RegexOptions.Compiled);

    public static bool IsMeeting(string text)
    {
        var course = RaceCourseNames.Parse(text);
        return (course != RaceCourse.Unknown && text == RaceCourseNames.GetJraName(course))
            || Meeting.IsMatch(text);
    }

    public static bool IsFollowingSection(string text) => text is
        "払戻金" or "勝馬の紹介" or "JRAからのお知らせ" or "Footer";
}

namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// カレンダーから取得できる、開催日とその日に開催される競馬場の一覧。
/// </summary>
public sealed record JraRaceDate(
    DateOnly Date,
    IReadOnlyList<RaceCourse> Courses);

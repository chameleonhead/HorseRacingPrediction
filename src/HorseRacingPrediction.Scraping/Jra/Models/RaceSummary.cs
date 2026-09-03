namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// レース一覧ページから取得できる最低限の情報。DOM解析の都合で推測して値を入れない。
/// </summary>
public sealed record RaceSummary(
    RaceId Id,
    string? Name,
    TimeOnly? StartTime,
    string? RaceCardUrl,
    string? ResultUrl)
{
    public int Number => Id.Number;
}

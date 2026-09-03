namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// 日付・競馬場・レース番号でレースを識別する。
/// JRA内部の開催回・開催日番号等は混ぜず、必要になったら別途 JraRaceKey を追加する。
/// </summary>
public sealed record RaceId
{
    public RaceId(DateOnly date, RaceCourse course, int number)
    {
        if (number is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        Date = date;
        Course = course;
        Number = number;
    }

    public DateOnly Date { get; init; }

    public RaceCourse Course { get; init; }

    public int Number { get; init; }
}

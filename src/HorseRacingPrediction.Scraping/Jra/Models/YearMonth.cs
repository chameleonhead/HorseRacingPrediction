namespace HorseRacingPrediction.Scraping.Jra.Models;

public readonly record struct YearMonth
{
    public YearMonth(int year, int month)
    {
        if (year is < 1900 or > 2200)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        Year = year;
        Month = month;
    }

    public int Year { get; init; }

    public int Month { get; init; }

    public DateOnly FirstDay
        => new(Year, Month, 1);

    public override string ToString()
        => $"{Year:D4}-{Month:D2}";
}

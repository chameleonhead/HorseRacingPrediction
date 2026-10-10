namespace HorseRacingPrediction.Web.Components.Races;

public enum RacePeriod
{
    All,
    Today,
    ThisWeek,
    PastMonth
}

public static class RacePeriodExtensions
{
    public static string ToQueryValue(this RacePeriod period) => period switch
    {
        RacePeriod.Today => "today",
        RacePeriod.ThisWeek => "week",
        RacePeriod.PastMonth => "past-month",
        _ => "all"
    };

    public static bool TryParse(string? value, out RacePeriod period)
    {
        period = value switch
        {
            "today" => RacePeriod.Today,
            "week" => RacePeriod.ThisWeek,
            "past-month" => RacePeriod.PastMonth,
            "all" => RacePeriod.All,
            _ => (RacePeriod)(-1)
        };

        return Enum.IsDefined(period);
    }

    public static (DateOnly? From, DateOnly? To) ResolveDates(this RacePeriod period, DateOnly today) => period switch
    {
        RacePeriod.Today => (today, today),
        RacePeriod.ThisWeek => ResolveWeek(today),
        RacePeriod.PastMonth => (today.AddMonths(-1), today),
        _ => (null, null)
    };

    public static DateOnly TodayInJapan(this TimeProvider timeProvider)
        => DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(9)).Date);

    private static (DateOnly From, DateOnly To) ResolveWeek(DateOnly today)
    {
        const int entryListPublicationDay = (int)DayOfWeek.Thursday;
        var daysSinceThursday = ((int)today.DayOfWeek - entryListPublicationDay + 7) % 7;
        var from = today.AddDays(-daysSinceThursday);
        return (from, from.AddDays(6));
    }
}

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed class JraCollectionSchedulePolicy : ICollectionSchedulePolicy
{
    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, Jst);
        var date = ParseDate(resource.Id);
        return resource.Type switch
        {
            ResourceType.RaceOdds => Odds(date, localNow),
            ResourceType.RaceCard => Card(date, localNow),
            ResourceType.RaceResult => Result(date, localNow, state),
            ResourceType.Horse or ResourceType.Jockey or ResourceType.Trainer => Profile(state, now),
            _ => new(false, null, CollectionPriority.Background, CollectionLane.Background, "immutable"),
        };
    }

    private static CollectionSchedule Odds(DateOnly? date, DateTimeOffset now)
    {
        if (date is null) return new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "missing-date");
        var delta = date.Value.ToDateTime(new TimeOnly(15, 30)) - now.DateTime;
        if (delta < TimeSpan.FromHours(-1)) return new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "race-ended");
        if (delta <= TimeSpan.FromMinutes(10)) return Due(now, TimeSpan.FromMinutes(1), CollectionPriority.Critical, "odds-final");
        if (delta <= TimeSpan.FromHours(1)) return Due(now, TimeSpan.FromMinutes(3), (CollectionPriority)90, "odds-near");
        if (delta <= TimeSpan.FromHours(6)) return Due(now, TimeSpan.FromMinutes(10), CollectionPriority.High, "odds-day");
        return new(false, new DateTimeOffset(date.Value.ToDateTime(new TimeOnly(9, 0)), now.Offset),
            CollectionPriority.High, CollectionLane.Realtime, "odds-not-open");
    }

    private static CollectionSchedule Card(DateOnly? date, DateTimeOffset now)
    {
        if (date is null) return new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "missing-date");
        var days = date.Value.DayNumber - DateOnly.FromDateTime(now.Date).DayNumber;
        if (days < 0) return new(false, null, CollectionPriority.Background, CollectionLane.Background, "historical-card");
        if (days == 0) return Due(now, TimeSpan.FromMinutes(10), (CollectionPriority)80, "race-day-card");
        if (days <= 7) return Due(now, TimeSpan.FromHours(3), CollectionPriority.High, "race-week-card");
        return new(false, new DateTimeOffset(date.Value.AddDays(-7).ToDateTime(new TimeOnly(9, 0)), now.Offset),
            CollectionPriority.Normal, CollectionLane.Normal, "future-card");
    }

    private static CollectionSchedule Result(DateOnly? date, DateTimeOffset now, CollectionStateSnapshot state)
    {
        if (state.Status == CollectionStateStatus.Current) return new(false, null,
            CollectionPriority.Background, CollectionLane.Background, "confirmed-result");
        if (date is null) return new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "missing-date");
        var start = new DateTimeOffset(date.Value.ToDateTime(new TimeOnly(9, 30)), now.Offset);
        if (now < start) return new(false, start, CollectionPriority.High, CollectionLane.Realtime, "result-not-due");
        return Due(now, TimeSpan.FromMinutes(5), CollectionPriority.Critical, "result-awaiting-confirmation");
    }

    private static CollectionSchedule Profile(CollectionStateSnapshot state, DateTimeOffset now)
    {
        var next = state.LastCollectedAt?.AddDays(30) ?? now;
        return new(next <= now, next <= now ? now.AddDays(30) : next,
            CollectionPriority.Low, CollectionLane.Normal, "profile-refresh");
    }

    private static CollectionSchedule Due(DateTimeOffset now, TimeSpan interval, CollectionPriority priority, string reason)
        => new(true, now.Add(interval), priority, CollectionLane.Realtime, reason);

    private static DateOnly? ParseDate(string id)
    {
        var token = id.Split([':', '/', '-'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var value in token)
        {
            if (value.Length == 8 && DateOnly.TryParseExact(value, "yyyyMMdd", out var compact)) return compact;
            if (DateOnly.TryParse(value, out var date)) return date;
        }
        return null;
    }
}

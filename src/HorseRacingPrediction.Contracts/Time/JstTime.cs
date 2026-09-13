using System.Globalization;

namespace HorseRacingPrediction.Contracts.Time;

public static class JstTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    public static DateTimeOffset Now(TimeProvider? timeProvider = null) =>
        (timeProvider ?? TimeProvider.System).GetUtcNow().ToOffset(Offset);

    public static DateTimeOffset Convert(DateTimeOffset value) => value.ToOffset(Offset);

    public static DateOnly Today(TimeProvider? timeProvider = null) =>
        DateOnly.FromDateTime(Now(timeProvider).DateTime);

    public static DateTime ToDatabase(DateTimeOffset value) =>
        DateTime.SpecifyKind(Convert(value).DateTime, DateTimeKind.Unspecified);

    public static DateTimeOffset FromDatabase(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), Offset);

    public static string ToDatabaseString(DateTimeOffset value) =>
        ToDatabase(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseStoredValue(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant)
            && (value.EndsWith('Z') || value.LastIndexOf('+') > 9 || value.LastIndexOf('-') > 9))
            return Convert(instant);

        return FromDatabase(DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces));
    }

    public static string Format(DateTimeOffset value, string format = "yyyy/MM/dd HH:mm") =>
        $"{Convert(value).ToString(format, CultureInfo.InvariantCulture)} JST";

    public static string Format(DateTimeOffset? value, string format = "yyyy/MM/dd HH:mm") =>
        value is null ? "—" : Format(value.Value, format);
}

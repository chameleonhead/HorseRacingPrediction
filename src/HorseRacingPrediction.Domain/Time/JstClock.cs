namespace HorseRacingPrediction.Domain.Time;

internal static class JstClock
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(Offset);
}

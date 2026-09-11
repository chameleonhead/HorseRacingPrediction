namespace HorseRacingPrediction.PredictionScheduling;

public sealed record PredictionCandidateLease(string RaceId, string LeaseToken);

public interface IPredictionSchedule
{
    Task EnqueueAsync(IEnumerable<string> raceIds, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PredictionCandidateLease>> AcquireAsync(DateTimeOffset now, TimeSpan minAge, int maxCount,
        TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(string raceId, string leaseToken, CancellationToken cancellationToken = default);
    Task<bool> RequeueAsync(string raceId, string leaseToken, DateTimeOffset availableAt, string? error,
        CancellationToken cancellationToken = default);
}

public sealed class PredictionScheduleOptions
{
    public const string SectionName = "PredictionScheduling";
    public string StateDirectory { get; set; } = string.Empty;
    public string StoreFileName { get; set; } = "prediction-executions.db";
}

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class ResultDayCollectionStatusEntity
{
    public string DayKey { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public int TargetYear { get; set; }
    public int TargetMonth { get; set; }
    public DateOnly TargetDate { get; set; }
    public ResultDayCollectionState Status { get; set; }
    public int ExpectedRaceCount { get; set; }
    public int CompletedRaceCount { get; set; }
    public string? IncompleteReason { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public DateTimeOffset? RetryAfter { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
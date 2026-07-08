namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class ProcessingMarkerEntity
{
    public string MarkerType { get; set; } = string.Empty;
    public string MarkerKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
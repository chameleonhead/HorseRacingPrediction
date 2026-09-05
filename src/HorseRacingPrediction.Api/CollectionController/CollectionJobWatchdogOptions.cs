namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionJobWatchdogOptions
{
    public const string SectionName = "CollectionJobWatchdog";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 5;
}

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed class CollectionPlatformOptions
{
    public const string SectionName = "CollectionPlatform";
    public string StateDirectory { get; set; } = "collection-platform-state";
    public string DatabaseFileName { get; set; } = "collection-platform.db";
    public int LeaseMinutes { get; set; } = 15;
}

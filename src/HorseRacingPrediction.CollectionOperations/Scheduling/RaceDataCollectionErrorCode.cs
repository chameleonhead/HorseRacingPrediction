namespace HorseRacingPrediction.Collector.Scheduling;

public enum RaceDataCollectionErrorCode
{
    Unknown = 0,
    UnsupportedProvider = 1,
    MetadataMissing = 2,
    NavigationFailed = 3,
    DiscoveryFailed = 4,
    ScrapeFailed = 5,
    SaveFailed = 6,
    ExternalRequestFailed = 7,
    NoDataFound = 8,
}
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record ResultDayCollectionRequestPayload(
    DateOnly RaceDate,
    string ProviderType,
    IReadOnlyList<JraRaceResultUrl> Urls,
    int ExpectedRaceCount);
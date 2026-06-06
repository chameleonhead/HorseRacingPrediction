using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record ResultDayCollectionRequestPayload(
    DateOnly RaceDate,
    string ProviderType,
    IReadOnlyList<JraRaceResultUrl> Urls,
    int ExpectedRaceCount);
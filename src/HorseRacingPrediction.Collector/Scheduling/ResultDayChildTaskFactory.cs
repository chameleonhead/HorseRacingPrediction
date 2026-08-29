using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Scheduling;

internal static class ResultDayChildTaskFactory
{
    public static IReadOnlyList<ResultDayChildTask> Create(ResultDayCollectionRequestPayload parent)
        => parent.Urls.Select(url => Create(parent, url)).ToList();

    private static ResultDayChildTask Create(ResultDayCollectionRequestPayload parent, JraRaceResultUrl url)
    {
        var racecourse = JraRacecourseResolver.ResolveDisplayName(url.Racecourse ?? url.RacecourseCode)
            ?? url.Racecourse
            ?? throw new InvalidOperationException($"競馬場を特定できません。URL={url.Url}");
        var raceNumber = url.RaceNumber
            ?? throw new InvalidOperationException($"レース番号を特定できません。URL={url.Url}");
        var raceId = DeterministicIdGenerator.BuildRaceId(parent.RaceDate, racecourse, raceNumber);
        var payload = new HistoricalRaceResultCollectionRequestPayload(
            parent.RaceDate, racecourse, raceNumber, raceId, parent.ProviderType);
        return new ResultDayChildTask(
            AgentJobKeyFactory.BuildHistoricalRaceResultCollectionRequestKey(
                parent.ProviderType, parent.RaceDate, racecourse, raceNumber),
            AgentJobPayloadSerializer.Serialize(payload));
    }
}

internal sealed record ResultDayChildTask(string DeduplicationKey, string Payload);

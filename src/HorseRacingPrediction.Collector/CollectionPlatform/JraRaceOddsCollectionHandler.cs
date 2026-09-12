using System.Net.Http.Json;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed class RaceOddsCollectionOptions
{
    public int EarlyIntervalMinutes { get; set; } = 10;
    public int WithinOneHourIntervalMinutes { get; set; } = 3;
    public int FinalIntervalMinutes { get; set; } = 1;
}

public interface IRaceOddsSnapshotSink
{
    Task SaveAsync(string raceId, JraRaceOddsPage page, CancellationToken cancellationToken);
}

public sealed class RaceOddsSnapshotApiClient(HttpClient client) : IRaceOddsSnapshotSink
{
    public async Task SaveAsync(string raceId, JraRaceOddsPage page, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync($"api/admin/races/{Uri.EscapeDataString(raceId)}/odds-snapshots",
            new RecordRaceOddsSnapshotRequest(page.ObservedAt,
                page.Entries.Select(x => new RaceOddsEntryRequest(x.HorseNumber, x.WinOdds, x.Popularity)).ToArray(),
                page.Entries.Select(x => new RaceOddsObservationRequest("Win", x.HorseNumber.ToString(),
                    x.WinOdds, x.Popularity)).ToArray()),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class JraRaceOddsCollectionHandler(IJraSessionFactory sessions, IRaceOddsSnapshotSink sink,
    IOptions<RaceOddsCollectionOptions> options, TimeProvider? timeProvider = null) : ICollectionDefinitionHandler
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public CollectionDefinitionId DefinitionId => new("race-odds");
    public ResourceType ResourceType => ResourceType.RaceOdds;
    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
    {
        var race = JraRaceCardCollectionHandler.ParseRaceId(task);
        await using var sessionLease = await JraSessionExecutionScope.AcquireAsync(sessions, token)
            .ConfigureAwait(false);
        var session = sessionLease.Session;
        JraRaceOddsPage? page = null;
        var locationOutcomes = new List<ResourceLocationOutcome>();
        foreach (var location in task.Locations ?? [])
        {
            try
            {
                var candidate = await session.Navigate.ToUrlAsync(location.Url, token).ConfigureAwait(false);
                if (candidate is JraRaceOddsPage odds && odds.RaceId == race)
                {
                    page = odds;
                    locationOutcomes.Add(ResourceLocationOutcomeClassifier.Succeeded(location));
                    break;
                }
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Unexpected(location, "RaceOddsIdentityMismatch"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Failed(location, ex));
            }
        }
        page ??= await session.Navigate.ToRaceOddsAsync(race, token).ConfigureAwait(false) as JraRaceOddsPage
            ?? throw new JraCollectionException("単勝オッズページを取得できませんでした。");
        if (page.RaceId != race) throw new JraCollectionException("オッズのRaceIdが対象と一致しません。");
        await sink.SaveAsync(task.Attributes.GetValueOrDefault("domainRaceId") ?? task.Resource.Id, page, token)
            .ConfigureAwait(false);
        var next = Next(task, page.ObservedAt);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: new Uri(page.Url), FinalUrl: new Uri(page.Url),
            PageIdentification: $"RaceOdds:JRA:{task.Resource.Id}", NextCollectionAt: next,
            LocationOutcomes: locationOutcomes);
    }

    private DateTimeOffset? Next(LeasedCollectionTask task, DateTimeOffset observedAt)
    {
        if (!TimeOnly.TryParse(task.Attributes.GetValueOrDefault("startTime"), out var start)
            || task.EffectiveDate is null) return observedAt.AddMinutes(options.Value.EarlyIntervalMinutes);
        var jst = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        var startAt = new DateTimeOffset(task.EffectiveDate.Value.ToDateTime(start), jst.GetUtcOffset(task.EffectiveDate.Value.ToDateTime(start)));
        if (observedAt >= startAt) return null;
        var remaining = startAt - observedAt;
        var minutes = remaining <= TimeSpan.FromMinutes(10) ? options.Value.FinalIntervalMinutes
            : remaining <= TimeSpan.FromHours(1) ? options.Value.WithinOneHourIntervalMinutes
            : options.Value.EarlyIntervalMinutes;
        return observedAt.AddMinutes(Math.Max(1, minutes));
    }
}

using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JraSiteDataCollectorProfileLookup : IJraProfileLookup
{
    public async Task<JraExtractionEnvelope<JraEntityProfile>> GetHorseProfileAsync(
        string horseName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestHorseProfileAsync(horseName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JraExtractionEnvelope<JraHorseProfileData>> GetHorseProfileWithHistoryAsync(
        string horseName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestHorseProfileWithHistoryAsync(horseName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JraExtractionEnvelope<JraEntityProfile>> GetJockeyProfileAsync(
        string jockeyName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestJockeyProfileAsync(jockeyName, cancellationToken).ConfigureAwait(false);
    }
}
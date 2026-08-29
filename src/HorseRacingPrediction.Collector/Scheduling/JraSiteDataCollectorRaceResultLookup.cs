using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JraSiteDataCollectorRaceResultLookup : IJraRaceResultLookup
{
    public async Task<JraExtractionEnvelope<JraRaceResultSummary>> GetRaceResultAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestRaceResultAsync(raceDate, racecourse, raceNumber, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JraExtractionEnvelope<JraRaceResultSummary>> GetRaceResultByUrlAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraSiteDataCollector.CreateAsync().ConfigureAwait(false);
        await taskAgent.NavigateAsync(sourceUrl, cancellationToken).ConfigureAwait(false);
        var extraction = await taskAgent.ExtractCurrentPageAsync(cancellationToken).ConfigureAwait(false);
        return extraction.ToTyped<JraRaceResultSummary>();
    }
}

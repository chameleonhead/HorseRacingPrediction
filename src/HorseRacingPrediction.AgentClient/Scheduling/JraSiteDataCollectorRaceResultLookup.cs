using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.AgentClient.Scheduling;

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
}
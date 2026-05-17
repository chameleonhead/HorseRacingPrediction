using HorseRacingPrediction.Agents.JraAgent;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class JraTaskAgentRaceResultLookup : IJraRaceResultLookup
{
    public async Task<JraExtractionEnvelope<JraRaceResultSummary>> GetRaceResultAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraTaskAgent.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestRaceResultAsync(raceDate, racecourse, raceNumber, cancellationToken).ConfigureAwait(false);
    }
}
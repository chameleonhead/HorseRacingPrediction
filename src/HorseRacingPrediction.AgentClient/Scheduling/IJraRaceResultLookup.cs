using HorseRacingPrediction.Agents.JraAgent;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public interface IJraRaceResultLookup
{
    Task<JraExtractionEnvelope<JraRaceResultSummary>> GetRaceResultAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        CancellationToken cancellationToken = default);
}
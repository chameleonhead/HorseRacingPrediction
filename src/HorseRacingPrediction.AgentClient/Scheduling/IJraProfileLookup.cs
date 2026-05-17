using HorseRacingPrediction.Agents.JraAgent;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public interface IJraProfileLookup
{
    Task<JraExtractionEnvelope<JraEntityProfile>> GetHorseProfileAsync(
        string horseName,
        CancellationToken cancellationToken = default);

    Task<JraExtractionEnvelope<JraEntityProfile>> GetJockeyProfileAsync(
        string jockeyName,
        CancellationToken cancellationToken = default);
}
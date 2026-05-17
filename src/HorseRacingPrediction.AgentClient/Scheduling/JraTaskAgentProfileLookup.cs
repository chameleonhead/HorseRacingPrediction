using HorseRacingPrediction.Agents.JraAgent;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class JraTaskAgentProfileLookup : IJraProfileLookup
{
    public async Task<JraExtractionEnvelope<JraEntityProfile>> GetHorseProfileAsync(
        string horseName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraTaskAgent.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestHorseProfileAsync(horseName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JraExtractionEnvelope<JraEntityProfile>> GetJockeyProfileAsync(
        string jockeyName,
        CancellationToken cancellationToken = default)
    {
        await using var taskAgent = await JraTaskAgent.CreateAsync().ConfigureAwait(false);
        return await taskAgent.RequestJockeyProfileAsync(jockeyName, cancellationToken).ConfigureAwait(false);
    }
}
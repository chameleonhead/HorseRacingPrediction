using HorseRacingPrediction.Agents.Plugins;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public static class AgentAcquisitionStatusKeyFactory
{
    public static string Build(
        AgentAcquisitionSubjectType subjectType,
        AgentAcquisitionOperationType operationType,
        string subjectName,
        string? relatedRaceId = null)
    {
        var normalizedName = DeterministicIdGenerator.NormalizeDisplayName(subjectName);
        var normalizedRaceId = string.IsNullOrWhiteSpace(relatedRaceId) ? null : relatedRaceId.Trim();
        return normalizedRaceId is null
            ? $"{subjectType}:{operationType}:{normalizedName}"
            : $"{subjectType}:{operationType}:{normalizedName}:{normalizedRaceId}";
    }
}
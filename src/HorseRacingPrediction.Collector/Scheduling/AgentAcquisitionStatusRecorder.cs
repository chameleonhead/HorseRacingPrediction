namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class AgentAcquisitionStatusRecorder
{
    private readonly IProcessingStateStore _stateStore;

    public AgentAcquisitionStatusRecorder(IProcessingStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public Task RecordAsync(
        AgentAcquisitionSubjectType subjectType,
        AgentAcquisitionOperationType operationType,
        string subjectName,
        RaceDataCollectionState status,
        string? providerType,
        string? subjectId,
        string? relatedRaceId,
        string? sourceUrl,
        RaceDataCollectionErrorCode? errorCode,
        string? errorReason,
        CancellationToken cancellationToken = default)
    {
        return _stateStore.UpsertAgentAcquisitionStatusAsync(
            AgentAcquisitionStatusKeyFactory.Build(subjectType, operationType, subjectName, relatedRaceId),
            subjectType,
            operationType,
            providerType,
            subjectId,
            subjectName,
            relatedRaceId,
            sourceUrl,
            status,
            errorCode,
            errorReason,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }
}

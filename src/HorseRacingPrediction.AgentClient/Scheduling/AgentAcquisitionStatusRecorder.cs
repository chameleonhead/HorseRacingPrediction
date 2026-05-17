namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class AgentAcquisitionStatusRecorder
{
    private readonly ProcessingStateStore _stateStore;

    public AgentAcquisitionStatusRecorder(ProcessingStateStore stateStore)
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
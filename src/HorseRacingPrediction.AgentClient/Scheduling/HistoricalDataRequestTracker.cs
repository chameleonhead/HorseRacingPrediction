namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class HistoricalDataRequestTracker
{
    private readonly ProcessingStateStore _stateStore;

    public HistoricalDataRequestTracker(ProcessingStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task<HistoricalRequestSummary> GetOutstandingRequestsAsync(
        string raceId,
        CancellationToken cancellationToken = default)
    {
        var horsePayloads = await _stateStore
            .GetActiveJobPayloadsAsync(AgentJobType.HorseHistoryCollectionRequest, cancellationToken)
            .ConfigureAwait(false);
        var jockeyPayloads = await _stateStore
            .GetActiveJobPayloadsAsync(AgentJobType.JockeyHistoryCollectionRequest, cancellationToken)
            .ConfigureAwait(false);
        var raceResultPayloads = await _stateStore
            .GetActiveJobPayloadsAsync(AgentJobType.HistoricalRaceResultCollectionRequest, cancellationToken)
            .ConfigureAwait(false);

        var pendingHorseRequests = horsePayloads
            .Select(AgentJobPayloadSerializer.Deserialize<HorseHistoryCollectionRequestPayload>)
            .Count(x => string.Equals(x.RequestedByRaceId, raceId, StringComparison.Ordinal));
        var pendingJockeyRequests = jockeyPayloads
            .Select(AgentJobPayloadSerializer.Deserialize<JockeyHistoryCollectionRequestPayload>)
            .Count(x => string.Equals(x.RequestedByRaceId, raceId, StringComparison.Ordinal));
        var pendingRaceResultRequests = raceResultPayloads
            .Select(AgentJobPayloadSerializer.Deserialize<HistoricalRaceResultCollectionRequestPayload>)
            .Count(x => string.Equals(x.RequestedByRaceId, raceId, StringComparison.Ordinal));

        return new HistoricalRequestSummary(pendingHorseRequests, pendingJockeyRequests, pendingRaceResultRequests);
    }
}
namespace HorseRacingPrediction.AgentClient.Scheduling;

public interface IHistoricalRaceResultCollector
{
    Task<HistoricalDataRequestExecutionResult> CollectAsync(
        HistoricalRaceResultCollectionRequestPayload payload,
        CancellationToken cancellationToken = default);
}
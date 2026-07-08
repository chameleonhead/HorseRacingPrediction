namespace HorseRacingPrediction.Collector.Scheduling;

public interface IHistoricalRaceResultCollector
{
    Task<HistoricalDataRequestExecutionResult> CollectAsync(
        HistoricalRaceResultCollectionRequestPayload payload,
        CancellationToken cancellationToken = default);
}
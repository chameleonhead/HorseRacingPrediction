namespace HorseRacingPrediction.Collector.Scheduling;

public interface IHistoricalDataRequestHandler
{
    string ProviderType { get; }

    Task<HistoricalDataRequestExecutionResult> HandleHorseHistoryRequestAsync(
        HorseHistoryCollectionRequestPayload payload,
        CancellationToken cancellationToken = default);

    Task<HistoricalDataRequestExecutionResult> HandleJockeyHistoryRequestAsync(
        JockeyHistoryCollectionRequestPayload payload,
        CancellationToken cancellationToken = default);

    Task<HistoricalDataRequestExecutionResult> HandleTrainerProfileRequestAsync(
        TrainerProfileCollectionRequestPayload payload,
        CancellationToken cancellationToken = default);
}

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record TrainerProfileCollectionRequestPayload(
    string TrainerId,
    string RequestedByRaceId,
    string ProviderType);

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record ResultBackfillPlanningRequestPayload(
    string ProviderType,
    int InitialBackfillYears);
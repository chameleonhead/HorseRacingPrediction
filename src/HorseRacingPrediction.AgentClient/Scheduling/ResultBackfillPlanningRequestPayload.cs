namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record ResultBackfillPlanningRequestPayload(
    string ProviderType,
    int InitialBackfillYears);
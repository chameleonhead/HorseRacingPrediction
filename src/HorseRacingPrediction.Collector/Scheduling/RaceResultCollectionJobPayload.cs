namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record RaceResultCollectionJobPayload(
    DateOnly RaceDate,
    string ProviderType,
    AgentWorkMode WorkMode,
    RaceResultAcquisitionOrigin Origin = RaceResultAcquisitionOrigin.Scheduled);

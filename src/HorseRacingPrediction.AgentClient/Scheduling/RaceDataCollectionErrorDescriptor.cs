namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed record RaceDataCollectionErrorDescriptor(
    RaceDataCollectionErrorCode Code,
    string Reason);
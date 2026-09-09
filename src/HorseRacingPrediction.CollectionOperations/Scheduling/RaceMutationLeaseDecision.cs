namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record RaceMutationLeaseDecision(
    bool Allowed,
    bool HasActiveLease,
    string? ActiveJobId,
    DateTimeOffset? LeaseExpiresAt);

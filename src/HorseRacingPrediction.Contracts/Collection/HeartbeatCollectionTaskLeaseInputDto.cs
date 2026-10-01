namespace HorseRacingPrediction.Contracts.Collection;

public sealed record HeartbeatCollectionTaskLeaseInputDto(string LeaseToken, int LeaseSeconds = 900);

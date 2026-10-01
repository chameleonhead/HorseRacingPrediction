using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record HeartbeatCollectionTaskLeaseRequest(HeartbeatCollectionTaskLeaseInputDto? Heartbeat)
{
    [JsonIgnore] public Guid Id { get; init; }
    [JsonIgnore] public string LeaseId { get; init; } = string.Empty;
}

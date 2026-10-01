using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CancelCollectionTaskRequest(CancelCollectionTaskInputDto? Cancellation)
{
    [JsonIgnore] public Guid Id { get; init; }
}

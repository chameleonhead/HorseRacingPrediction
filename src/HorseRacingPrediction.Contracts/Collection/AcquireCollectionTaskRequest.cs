using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record AcquireCollectionTaskRequest(AcquireCollectionTaskInputDto? Acquisition)
{
    [JsonIgnore] public Guid Id { get; init; }
}

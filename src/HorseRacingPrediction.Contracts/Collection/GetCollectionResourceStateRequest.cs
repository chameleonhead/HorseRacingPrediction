using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record GetCollectionResourceStateRequest
{
    [JsonIgnore] public CollectionResourceType Type { get; init; }
    [JsonIgnore] public string Provider { get; init; } = string.Empty;
    [JsonIgnore] public string ResourceId { get; init; } = string.Empty;
    [JsonIgnore] public string DefinitionId { get; init; } = string.Empty;
}

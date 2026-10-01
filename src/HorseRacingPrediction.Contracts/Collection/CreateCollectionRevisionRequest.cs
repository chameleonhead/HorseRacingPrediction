using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CreateCollectionRevisionRequest(CreateCollectionRevisionInputDto? Revision)
{
    [JsonIgnore] public string Definition { get; init; } = string.Empty;
}

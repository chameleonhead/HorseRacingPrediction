using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record GetBackfillBatchRequest
{
    [JsonIgnore] public string Id { get; init; } = string.Empty;
}

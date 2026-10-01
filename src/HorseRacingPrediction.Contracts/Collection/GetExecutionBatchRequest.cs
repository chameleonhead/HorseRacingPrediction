using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record GetExecutionBatchRequest
{
    [JsonIgnore] public Guid Id { get; init; }
}

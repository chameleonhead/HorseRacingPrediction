using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CompleteCollectionTaskAttemptRequest(CompleteCollectionTaskAttemptInputDto? Attempt)
{
    [JsonIgnore] public Guid Id { get; init; }
}

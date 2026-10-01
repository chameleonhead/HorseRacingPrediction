using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record TransitionCollectionExecutionRequest(TransitionCollectionExecutionInputDto? Transition)
{
    [JsonIgnore] public Guid Id { get; init; }
}

using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed record GetRaceCollectionReadinessRequest
{
    [JsonIgnore] public string RaceId { get; init; } = string.Empty;
}

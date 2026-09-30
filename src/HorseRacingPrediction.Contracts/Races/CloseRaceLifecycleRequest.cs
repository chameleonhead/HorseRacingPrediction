using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record CloseRaceLifecycleRequest
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

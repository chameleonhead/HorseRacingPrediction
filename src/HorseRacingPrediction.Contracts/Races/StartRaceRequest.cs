using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record StartRaceRequest
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

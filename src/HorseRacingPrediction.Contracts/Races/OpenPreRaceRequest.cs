using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record OpenPreRaceRequest
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

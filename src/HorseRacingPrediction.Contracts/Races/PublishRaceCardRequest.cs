using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record PublishRaceCardRequest(PublishRaceCardInputDto? Card)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

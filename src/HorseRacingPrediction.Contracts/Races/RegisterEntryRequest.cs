using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record RegisterEntryRequest(RegisterEntryInputDto? Entry)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

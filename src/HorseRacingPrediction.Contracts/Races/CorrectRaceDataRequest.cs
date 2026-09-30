
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record CorrectRaceDataRequest(CorrectRaceDataInputDto? Race)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

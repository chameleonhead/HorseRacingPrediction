using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record RecordWeatherObservationRequest(RecordWeatherObservationInputDto? Observation)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

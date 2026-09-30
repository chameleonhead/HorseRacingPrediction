using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record RecordTrackConditionRequest(RecordTrackConditionInputDto? Observation)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

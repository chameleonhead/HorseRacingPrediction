
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record MarkRaceRescheduledRequest(MarkRaceRescheduledInputDto? Reschedule)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

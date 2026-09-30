using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record DeclareRaceResultRequest(DeclareRaceResultInputDto? Result)
{
    [JsonIgnore]
    public string RaceId { get; init; } = string.Empty;
}

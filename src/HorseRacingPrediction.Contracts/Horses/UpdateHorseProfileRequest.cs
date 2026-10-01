using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record UpdateHorseProfileRequest
{
    [JsonIgnore]
    public string HorseId { get; init; } = string.Empty;

    public UpdateHorseProfileInputDto? Horse { get; init; }
}

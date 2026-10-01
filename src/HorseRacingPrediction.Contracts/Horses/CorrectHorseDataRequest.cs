using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record CorrectHorseDataRequest
{
    [JsonIgnore]
    public string HorseId { get; init; } = string.Empty;

    public CorrectHorseDataInputDto? Horse { get; init; }
}

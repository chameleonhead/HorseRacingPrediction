using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Owners;

public sealed record UpdateOwnerRequest
{
    [JsonIgnore]
    public string OwnerId { get; init; } = string.Empty;

    public UpdateOwnerInputDto? Owner { get; init; }
}

using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Owners;

public sealed record MergeOwnerRequest
{
    [JsonIgnore]
    public string OwnerId { get; init; } = string.Empty;

    public MergeOwnerInputDto? Merge { get; init; }
}

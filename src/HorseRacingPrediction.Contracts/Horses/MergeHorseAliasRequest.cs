using System.Text.Json.Serialization;
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Horses;

public sealed record MergeHorseAliasRequest
{
    [JsonIgnore]
    public string HorseId { get; init; } = string.Empty;

    public MergeAliasInputDto? Alias { get; init; }
}

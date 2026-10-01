using System.Text.Json.Serialization;
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record MergeJockeyAliasRequest
{
    [JsonIgnore]
    public string JockeyId { get; init; } = string.Empty;

    public MergeAliasInputDto? Alias { get; init; }
}

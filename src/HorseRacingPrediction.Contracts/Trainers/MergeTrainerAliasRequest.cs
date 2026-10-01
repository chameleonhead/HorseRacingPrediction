using System.Text.Json.Serialization;
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record MergeTrainerAliasRequest
{
    [JsonIgnore]
    public string TrainerId { get; init; } = string.Empty;

    public MergeAliasInputDto? Alias { get; init; }
}

using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record CorrectTrainerDataRequest
{
    [JsonIgnore]
    public string TrainerId { get; init; } = string.Empty;

    public CorrectTrainerDataInputDto? Trainer { get; init; }
}

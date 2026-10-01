using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record UpdateTrainerProfileRequest
{
    [JsonIgnore]
    public string TrainerId { get; init; } = string.Empty;

    public UpdateTrainerProfileInputDto? Trainer { get; init; }
}

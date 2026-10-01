using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record GetTrainerProfileRequest([property: JsonIgnore] string TrainerId);

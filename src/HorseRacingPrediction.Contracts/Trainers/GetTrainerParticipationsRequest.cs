using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record GetTrainerParticipationsRequest(
    [property: JsonIgnore] string TrainerId,
    int? Take,
    int? Skip);

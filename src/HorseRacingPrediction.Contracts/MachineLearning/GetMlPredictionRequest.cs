using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.MachineLearning;

public sealed record GetMlPredictionRequest([property: JsonIgnore] string RaceId);
